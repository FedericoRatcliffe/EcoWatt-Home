namespace EcoWattCasa.Domain.ValueObjects;

/// <summary>
/// Resultado de una agregacion hecha en SQL sobre un bucket de tiempo. Trae los datos
/// crudos que necesita el calculo de energia: el delta del contador acumulado cuando
/// existe, y el promedio de potencia como respaldo.
/// </summary>
/// <param name="FirstTotalKwh">ENERGY.Total de la primera lectura del bucket, en orden temporal.</param>
/// <param name="LastTotalKwh">ENERGY.Total de la ultima lectura del bucket, en orden temporal.</param>
/// <param name="AvgWatts">Promedio de potencia instantanea del bucket.</param>
/// <param name="SampleCount">Cantidad de muestras, para saber si el dato es confiable.</param>
public readonly record struct EnergySamples(
    double? FirstTotalKwh,
    double? LastTotalKwh,
    double AvgWatts,
    double MaxWatts,
    int SampleCount,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp);

/// <summary>Agregacion de un bucket temporal (hora o dia) para un dispositivo.</summary>
public sealed record BucketEnergySamples(DateTimeOffset BucketStartUtc, Guid DeviceId, EnergySamples Samples);
