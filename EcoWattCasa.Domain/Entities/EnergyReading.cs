namespace EcoWattCasa.Domain.Entities;

/// <summary>Una muestra de consumo publicada por un dispositivo.</summary>
public class EnergyReading
{
    public long Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>Momento de la muestra, siempre en UTC (columna timestamptz).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Potencia activa instantanea en W (ENERGY.Power).</summary>
    public double Watts { get; set; }

    public double Voltage { get; set; }

    /// <summary>Corriente en A (ENERGY.Current).</summary>
    public double Amperage { get; set; }

    /// <summary>Energia acumulada del medidor desde TotalStartTime, en kWh (ENERGY.Total).</summary>
    public double? TotalKwh { get; set; }

    /// <summary>Energia del dia segun el propio dispositivo, en kWh (ENERGY.Today).</summary>
    public double? TodayKwh { get; set; }

    /// <summary>Factor de potencia (ENERGY.Factor).</summary>
    public double? PowerFactor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
