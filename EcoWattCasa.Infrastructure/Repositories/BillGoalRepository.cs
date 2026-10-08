using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoWattCasa.Infrastructure.Repositories;

/// <summary>La tabla tiene a lo sumo una fila: el objetivo es de la casa, no de un periodo.</summary>
public sealed class BillGoalRepository(EcoWattDbContext db) : IBillGoalRepository
{
    public async Task<BillGoal?> GetAsync(CancellationToken ct = default)
        => await db.BillGoals.AsNoTracking().OrderByDescending(g => g.UpdatedAt).FirstOrDefaultAsync(ct);

    public async Task<BillGoal> SetAsync(decimal amount, CancellationToken ct = default)
    {
        var goal = await db.BillGoals.OrderByDescending(g => g.UpdatedAt).FirstOrDefaultAsync(ct);

        if (goal is null)
        {
            goal = new BillGoal { Id = Guid.NewGuid() };
            db.BillGoals.Add(goal);
        }

        goal.Amount = amount;
        goal.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return goal;
    }

    public async Task<bool> ClearAsync(CancellationToken ct = default)
        => await db.BillGoals.ExecuteDeleteAsync(ct) > 0;
}
