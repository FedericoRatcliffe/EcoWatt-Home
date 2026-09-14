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
    public static async Task<int> RunAsync(SimulatedDevice[] devices, MockOptions options)
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

        var pending = new List<EnergyReading>(capacity: 10_000);
        var inserted = 0;

        foreach (var device in devices)
        {
            if (!registered.TryGetValue(device.Topic, out var entity))
            {
                Console.WriteLine($"  ! {device.Topic} no esta registrado en la base, se omite.");
                continue;
            }

            // Se borra lo que hubiera en la ventana para que correr el backfill dos veces
            // no duplique el consumo.
            var deleted = await db.EnergyReadings
                .Where(r => r.DeviceId == entity.Id && r.Timestamp >= from && r.Timestamp <= to)
                .ExecuteDeleteAsync();

            if (deleted > 0)
                Console.WriteLine($"  {device.Topic}: {deleted} lecturas previas de la ventana borradas.");

            for (var t = from; t < to; t += step)
            {
                var sample = device.Step(step, t, rng);
                pending.Add(new EnergyReading
                {
                    DeviceId = entity.Id,
                    Timestamp = t,
                    Watts = sample.Watts,
                    Voltage = sample.Voltage,
                    Amperage = sample.Current,
                    TotalKwh = sample.TotalKwh,
                    TodayKwh = sample.TodayKwh,
                    PowerFactor = sample.Factor,
                    CreatedAt = t
                });

                if (pending.Count >= 5_000)
                    inserted += await FlushAsync(db, pending);
            }

            inserted += await FlushAsync(db, pending);
            Console.WriteLine($"  {device.Topic}: listo.");
        }

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
