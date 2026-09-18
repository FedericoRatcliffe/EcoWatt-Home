using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EcoWattCasa.MockDevices;

/// <summary>
/// Rellena historia pasada escribiendo en Postgres. Hace falta porque la ingesta MQTT
/// timestampea con la hora del servidor: por el broker no se puede simular ayer.
/// Sirve para tener los graficos de 7 y 30 dias con datos desde el primer arranque.
/// </summary>
internal static class Backfill
{
    public static async Task<int> RunAsync(Fleet fleet, MockOptions options)
    {
        var dbOptions = new DbContextOptionsBuilder<EcoWattDbContext>()
            .UseNpgsql(options.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new EcoWattDbContext(dbOptions);

        try
        {
            await DbSeeder.MigrateAndSeedAsync(db, NullLogger.Instance);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No se pudo conectar a Postgres: {ex.Message}");
            Console.WriteLine("Levanta la base con: docker compose up -d postgres");
            return 1;
        }

        var registered = await db.Devices.ToDictionaryAsync(d => d.MqttTopic, StringComparer.OrdinalIgnoreCase);
        var step = options.BackfillStep;
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-options.BackfillDays);
        var rng = new Random(options.Seed);

        Console.WriteLine($"Backfill de {options.BackfillDays} dia(s), una muestra cada {step.TotalMinutes:0} min.");

        // Se resuelven todos los equipos antes de simular: el medidor mide la suma de los
        // enchufes, asi que hay que avanzar la flota entera en cada paso de tiempo, no un
        // equipo completo y despues el siguiente.
        var targets = new List<(SimulatedDevice Device, Device Entity)>();

        foreach (var device in fleet.All)
        {
            if (registered.TryGetValue(device.Topic, out var entity))
                targets.Add((device, entity));
            else
                Console.WriteLine($"  ! {device.Topic} no esta registrado en la base, se omite.");
        }

        if (targets.Count == 0)
        {
            Console.WriteLine("Ningun dispositivo simulado esta en la base. Nada que rellenar.");
            return 1;
        }

        // Se borra lo que hubiera en la ventana para que correr el backfill dos veces
        // no duplique el consumo.
        foreach (var (device, entity) in targets)
        {
            var deleted = await db.EnergyReadings
                .Where(r => r.DeviceId == entity.Id && r.Timestamp >= from && r.Timestamp <= to)
                .ExecuteDeleteAsync();

            if (deleted > 0)
                Console.WriteLine($"  {device.Topic}: {deleted} lecturas previas de la ventana borradas.");
        }

        var byTopic = targets.ToDictionary(t => t.Device.Topic, t => t.Entity, StringComparer.OrdinalIgnoreCase);
        var pending = new List<EnergyReading>(capacity: 10_000);
        var inserted = 0;

        for (var t = from; t < to; t += step)
        {
            foreach (var (device, sample) in fleet.Step(step, t, rng))
            {
                if (!byTopic.TryGetValue(device.Topic, out var entity))
                    continue;

                // Se guarda el canal que tiene configurado el dispositivo, igual que hace
                // la ingesta MQTT: en el EM2 el canal 1 no tiene pinza y no representa nada.
                if (entity.ChannelIndex >= sample.Channels.Length)
                {
                    Console.WriteLine(
                        $"  ! {device.Topic} apunta al canal {entity.ChannelIndex} pero simula " +
                        $"{sample.Channels.Length}. Revisa ChannelIndex.");
                    byTopic.Remove(device.Topic);
                    continue;
                }

                var channel = sample.Channels[entity.ChannelIndex];
                pending.Add(new EnergyReading
                {
                    DeviceId = entity.Id,
                    Timestamp = t,
                    Watts = channel.Watts,
                    Voltage = sample.Voltage,
                    Amperage = channel.Current,
                    TotalKwh = channel.TotalKwh,
                    TodayKwh = channel.TodayKwh,
                    PowerFactor = channel.Factor,
                    CreatedAt = t
                });
            }

            if (pending.Count >= 5_000)
                inserted += await FlushAsync(db, pending);
        }

        inserted += await FlushAsync(db, pending);

        Console.WriteLine($"Backfill terminado: {inserted} lecturas entre {from:yyyy-MM-dd HH:mm} y {to:yyyy-MM-dd HH:mm} UTC.");
        return 0;
    }

    private static async Task<int> FlushAsync(EcoWattDbContext db, List<EnergyReading> pending)
    {
        if (pending.Count == 0)
            return 0;

        db.EnergyReadings.AddRange(pending);
        await db.SaveChangesAsync();

        // Sin limpiar el change tracker el consumo de memoria crece linealmente con el backfill.
        db.ChangeTracker.Clear();

        var count = pending.Count;
        pending.Clear();
        return count;
    }
}
