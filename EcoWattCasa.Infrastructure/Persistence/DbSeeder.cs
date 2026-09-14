using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EcoWattCasa.Infrastructure.Persistence;

/// <summary>
/// Datos minimos para que el dashboard tenga sentido en el primer arranque: el cuadro
/// tarifario vigente y los dispositivos que publica el mock. Es idempotente.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Topics que publica EcoWattCasa.MockDevices. Los consumos estan calibrados contra las
    /// facturas reales de la casa (~175 kWh/mes, 243 W promedio): estos tres suman ~95 kWh/mes,
    /// algo mas de la mitad del total, que es lo esperable al medir solo algunos enchufes.
    /// </summary>
    public static readonly (string Topic, string Name, string Location, int NominalWatts)[] SimulatedDevices =
    [
        ("sonoff-pc", "PC + monitores", "Escritorio", 240),
        ("sonoff-heladera", "Heladera", "Cocina", 130),
        ("sonoff-lavarropas", "Lavarropas", "Lavadero", 450)
    ];

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

        foreach (var (topic, name, location, watts) in SimulatedDevices)
        {
            if (await db.Devices.AnyAsync(d => d.MqttTopic == topic, ct))
                continue;

            db.Devices.Add(new Device
            {
                Id = Guid.NewGuid(),
                Name = name,
                MqttTopic = topic,
                Location = location,
                NominalWatts = watts,
                Type = DeviceType.Simulated,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

            logger.LogInformation("Dispositivo simulado cargado: {Name} ({Topic})", name, topic);
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
