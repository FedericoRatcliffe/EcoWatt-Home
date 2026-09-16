namespace EcoWattCasa.Domain.Entities;

/// <summary>
/// Una hora ya consolidada de un dispositivo.
///
/// Las lecturas crudas llegan cada 10 s: 25.920 filas por dia con tres enchufes, ~9,5 M al
/// año. Consultar un mes sobre eso obliga a escanear cientos de miles de filas cada vez que
/// el dashboard refresca. Esta tabla guarda el mismo agregado que necesita el calculo, una
/// fila por hora y dispositivo (~26.000 al año), y las crudas se borran pasada la retencion.
///
/// Los campos son exactamente los de <see cref="ValueObjects.EnergySamples"/>: el delta del
/// contador en orden temporal y el promedio de potencia como respaldo.
/// </summary>
public class EnergyHourly
{
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>Comienzo de la hora, en UTC.</summary>
    public DateTimeOffset HourUtc { get; set; }

    /// <summary>ENERGY.Total de la primera lectura de la hora, en orden temporal.</summary>
    public double? FirstTotalKwh { get; set; }

    /// <summary>ENERGY.Total de la ultima lectura de la hora.</summary>
    public double? LastTotalKwh { get; set; }

    public double AvgWatts { get; set; }
    public double MaxWatts { get; set; }
    public int SampleCount { get; set; }

    public DateTimeOffset FirstTimestamp { get; set; }
    public DateTimeOffset LastTimestamp { get; set; }

    /// <summary>Cuando se consolido. Sirve para rehacer un rango si hiciera falta.</summary>
    public DateTimeOffset RolledUpAt { get; set; }
}
