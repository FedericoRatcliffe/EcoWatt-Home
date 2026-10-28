using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EcoWattCasa.Infrastructure.Persistence;

/// <summary>
/// Datos minimos para que el dashboard tenga sentido en el primer arranque: el cuadro
/// tarifario vigente y los cinco dispositivos del hardware comprado. Es idempotente.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Los cinco equipos Athom comprados, con el mismo topic que hay que configurar en cada
    /// uno por la consola de Tasmota.
    ///
    /// Los cuatro enchufes miden ZAPATILLAS, no aparatos sueltos: cada uno cuelga de una
    /// zapatilla con PC, monitores, home theater y luces. Eso tiene dos consecuencias.
    ///
    /// La primera es el limite: los enchufes que llegaron son de 10A (2.200 W) y el tope
    /// aplica a la SUMA de la zapatilla, no a un aparato. Ver README, "Enchufes de 10A".
    ///
    /// La segunda es el rele: van los cuatro bloqueados. Cortarle la corriente de golpe a una
    /// PC encendida es trabajo perdido, y en el peor caso un sistema de archivos danado. Se
    /// desbloquea a mano el que se quiera poder conmutar.
    ///
    /// Los consumos nominales son estimaciones del pico de cada zapatilla; se corrigen cuando
    /// haya medicion real. Son solo informativos: no alimentan ningun calculo.
    /// </summary>
    public static readonly DeviceSeed[] Devices =
    [
        // El medidor de tablero: su lectura es el total de la casa e incluye a los enchufes.
        new("em2-tablero", "Medidor de tablero", "Tablero principal", 7_040,
            DeviceType.AthomEm2, DeviceRole.HouseMeter,
            ChannelIndex: 0, RelayLocked: false, MinRelayIntervalSeconds: 0),

        new("plug-paula", "Zapatilla Paula", "Paula", 500,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: true, MinRelayIntervalSeconds: 60),

        new("plug-andy-1", "Zapatilla Andy 1", "Andy", 600,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: true, MinRelayIntervalSeconds: 60),

        // Solo un televisor colgado de una zapatilla, asi que el nominal es bajo.
        new("plug-fede-habitacion", "Zapatilla Fede Habitacion", "Fede", 150,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: true, MinRelayIntervalSeconds: 60),

        // La mas cargada: Ryzen 5 5500 + RX 7600 dan ~350 W de la pared jugando, y con
        // monitores, audio y luces la zapatilla llega a ~880 W de pico.
        new("plug-fede", "Zapatilla Fede", "Fede", 900,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: true, MinRelayIntervalSeconds: 60)
    ];

    public sealed record DeviceSeed(
        string Topic,
        string Name,
        string Location,
        int NominalWatts,
        DeviceType Type,
        DeviceRole Role,
        int ChannelIndex,
        bool RelayLocked,
        int MinRelayIntervalSeconds);

    public static async Task MigrateAndSeedAsync(EcoWattDbContext db, ILogger logger, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.TariffSchedules.AnyAsync(ct))
        {
            db.TariffSchedules.Add(BuildInitialTariff());
            logger.LogInformation(
                "Cuadro tarifario inicial cargado: Res Sin Sub VT vigente desde 2026-06-30 " +
                "(tomado de la factura 09/2026). Importa tus PDFs para cargar el historial completo.");
        }

        foreach (var seed in Devices)
        {
            if (await db.Devices.AnyAsync(d => d.MqttTopic == seed.Topic, ct))
                continue;

            db.Devices.Add(new Device
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                MqttTopic = seed.Topic,
                Location = seed.Location,
                NominalWatts = seed.NominalWatts,
                Type = seed.Type,
                Role = seed.Role,
                ChannelIndex = seed.ChannelIndex,
                RelayLocked = seed.RelayLocked,
                MinRelayIntervalSeconds = seed.MinRelayIntervalSeconds,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

            logger.LogInformation(
                "Dispositivo cargado: {Name} ({Topic}, {Type}/{Role})",
                seed.Name, seed.Topic, seed.Type, seed.Role);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tarifa "Res Sin Sub VT" de la Cooperativa de Venado Tuerto, vigente desde el 30/06/2026.
    /// Los valores salen de la factura 09/2026 y reconstruyen su total exacto ($78.995,13).
    /// </summary>
    private static TariffSchedule BuildInitialTariff()
    {
        var schedule = new TariffSchedule
        {
            Id = Guid.NewGuid(),
            ValidFrom = new DateOnly(2026, 6, 30),
            FixedChargePerDay = 122.0348m,
            Source = "Factura 09/2026 (semilla)",
            CreatedAt = DateTimeOffset.UtcNow
        };

        schedule.Blocks.Add(new TariffBlock
        {
            Id = Guid.NewGuid(), Order = 1, Label = "Hasta 75 kWh", UpToKwh = 75, PricePerKwh = 243.73m
        });
        schedule.Blocks.Add(new TariffBlock
        {
            Id = Guid.NewGuid(), Order = 2, Label = "Hasta 150 kWh", UpToKwh = 150, PricePerKwh = 265.01m
        });
        schedule.Blocks.Add(new TariffBlock
        {
            Id = Guid.NewGuid(), Order = 3, Label = "Hasta 300 kWh", UpToKwh = 300, PricePerKwh = 354.34m
        });

        // Los cuatro recargos porcentuales suman 42% del importe basico.
        schedule.Surcharges.Add(new TariffSurcharge { Id = Guid.NewGuid(), Name = "IVA 21% Energia", Rate = 0.21m });
        schedule.Surcharges.Add(new TariffSurcharge { Id = Guid.NewGuid(), Name = "Cap.Inv.Bienes de Uso", Rate = 0.1215m });
        schedule.Surcharges.Add(new TariffSurcharge { Id = Guid.NewGuid(), Name = "Ley Pcial. 10014", Rate = 0.06m });
        schedule.Surcharges.Add(new TariffSurcharge { Id = Guid.NewGuid(), Name = "Cap.Rem.L.B.T.", Rate = 0.0285m });

        schedule.PeriodCharges.Add(new TariffPeriodCharge
        {
            Id = Guid.NewGuid(), Name = "Tasa de Alum. Pub.", Amount = 6979.00m
        });
        schedule.PeriodCharges.Add(new TariffPeriodCharge
        {
            Id = Guid.NewGuid(), Name = "Ley Pcial. 12692 - Energias Renovables", Amount = 230.81m
        });

        return schedule;
    }
}
