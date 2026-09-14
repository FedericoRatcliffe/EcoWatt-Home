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

    /// <summary>Agregado por hora UTC y dispositivo (base de los graficos horarios, diarios y mensuales).</summary>
    Task<IReadOnlyList<BucketEnergySamples>> GetHourlySamplesAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
