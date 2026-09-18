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
    /// uno por la consola de Tasmota. Los consumos nominales estan calibrados contra las
    /// facturas reales de la casa (~175 kWh/mes, 243 W promedio).
    /// </summary>
    public static readonly DeviceSeed[] Devices =
    [
        // El medidor de tablero: su lectura es el total de la casa e incluye a los enchufes.
        new("em2-tablero", "Medidor de tablero", "Tablero principal", 7_040,
            DeviceType.AthomEm2, DeviceRole.HouseMeter,
            ChannelIndex: 0, RelayLocked: false, MinRelayIntervalSeconds: 0),

        // La heladera lleva el rele bloqueado: un corte por error arruina la comida, y el
        // minimo de 10 minutos protege al compresor del ciclado corto.
        new("plug-heladera", "Heladera", "Cocina", 130,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: true, MinRelayIntervalSeconds: 600),

        new("plug-pc", "PC + monitores", "Escritorio", 240,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: false, MinRelayIntervalSeconds: 60),

        new("plug-lavarropas", "Lavarropas", "Lavadero", 450,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: false, MinRelayIntervalSeconds: 60),

        // El cuarto enchufe queda sin asignar hasta que se decida que medir.
        new("plug-libre", "Enchufe libre", "Sin asignar", 0,
            DeviceType.AthomPlugV3, DeviceRole.Appliance,
            ChannelIndex: 0, RelayLocked: false, MinRelayIntervalSeconds: 60)
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
