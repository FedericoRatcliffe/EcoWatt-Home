using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoWattCasa.Infrastructure.Repositories;

public sealed class RelayCommandRepository(EcoWattDbContext db) : IRelayCommandRepository
{
    public async Task AddAsync(RelayCommand command, CancellationToken ct = default)
    {
        db.RelayCommands.Add(command);
        await db.SaveChangesAsync(ct);
    }

    public async Task<RelayCommand?> GetLastSentAsync(Guid deviceId, CancellationToken ct = default)
        => await db.RelayCommands
            .AsNoTracking()
            // Solo los que salieron: un comando rechazado no movio el rele, asi que no
            // corresponde que extienda la ventana de tiempo minimo.
            .Where(c => c.DeviceId == deviceId && c.Outcome == RelayCommandOutcome.Sent)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RelayCommand>> GetRecentAsync(
        Guid? deviceId, int limit = 50, CancellationToken ct = default)
    {
        var query = db.RelayCommands.AsNoTracking();

        if (deviceId is { } id)
            query = query.Where(c => c.DeviceId == id);

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }
}
