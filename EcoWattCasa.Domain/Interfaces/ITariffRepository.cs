using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Domain.Interfaces;

public interface ITariffRepository
{
    /// <summary>Cuadro tarifario vigente hoy, con tramos y recargos cargados.</summary>
    Task<TariffSchedule?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Cuadro que regia en una fecha dada.</summary>
    Task<TariffSchedule?> GetForDateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Serie completa ordenada por vigencia. Un periodo se costea dia por dia con esta serie.</summary>
    Task<IReadOnlyList<TariffSchedule>> GetHistoryAsync(CancellationToken ct = default);

    /// <summary>Guarda un cuadro nuevo, o reemplaza el que tenga la misma vigencia.</summary>
    Task<TariffSchedule> UpsertAsync(TariffSchedule schedule, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
