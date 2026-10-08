using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Domain.Interfaces;

public interface IBillGoalRepository
{
    Task<BillGoal?> GetAsync(CancellationToken ct = default);

    /// <summary>Fija el objetivo de la casa, reemplazando el que hubiera.</summary>
    Task<BillGoal> SetAsync(decimal amount, CancellationToken ct = default);

    Task<bool> ClearAsync(CancellationToken ct = default);
}
