using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.ValueObjects;

namespace EcoWattCasa.Domain.Interfaces;

public interface IEnergyReadingRepository
{
    Task AddAsync(EnergyReading reading, CancellationToken ct = default);

    /// <summary>Historial crudo de un dispositivo. Acotado por <paramref name="maxPoints"/> para no volcar millones de filas.</summary>
    Task<IReadOnlyList<EnergyReading>> GetByDeviceAsync(
        Guid deviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints = 2000, CancellationToken ct = default);

    /// <summary>Ultima lectura de cada dispositivo, para las cards de consumo actual.</summary>
    Task<IReadOnlyList<EnergyReading>> GetLatestPerDeviceAsync(CancellationToken ct = default);

    /// <summary>
    /// Agregado por hora UTC y dispositivo: la base de los graficos horarios, diarios y
    /// mensuales. Sale de la tabla de horas consolidadas, y de las lecturas crudas para las
    /// horas que el rollup todavia no proceso.
    /// </summary>
    Task<IReadOnlyList<BucketEnergySamples>> GetHourlySamplesAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>
    /// Consolida en la tabla horaria las horas ya cerradas que todavia no estan, y despues
    /// borra las lecturas crudas anteriores a la retencion.
    /// </summary>
    /// <returns>Horas consolidadas y lecturas crudas borradas.</returns>
    Task<(int HoursRolledUp, int RawDeleted)> RollUpAndPruneAsync(
        DateTimeOffset completeBefore, DateTimeOffset deleteRawBefore, CancellationToken ct = default);
}
