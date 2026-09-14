using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoWattCasa.Infrastructure.Repositories;

public sealed class TariffRepository(EcoWattDbContext db) : ITariffRepository
{
    public Task<TariffSchedule?> GetCurrentAsync(CancellationToken ct = default)
        => GetForDateAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);

    public async Task<TariffSchedule?> GetForDateAsync(DateOnly date, CancellationToken ct = default)
        => await WithDetails()
               .Where(t => t.ValidFrom <= date)
               .OrderByDescending(t => t.ValidFrom)
               .FirstOrDefaultAsync(ct)
           // Una fecha anterior al primer cuadro cargado se costea con el mas viejo que haya.
           ?? await WithDetails()
               .OrderBy(t => t.ValidFrom)
               .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TariffSchedule>> GetHistoryAsync(CancellationToken ct = default)
        => await WithDetails()
            .OrderBy(t => t.ValidFrom)
            .ToListAsync(ct);

    public async Task<TariffSchedule> UpsertAsync(TariffSchedule schedule, CancellationToken ct = default)
    {
        // La vigencia es unica: volver a cargar la misma fecha reemplaza el cuadro entero,
        // con sus tramos y recargos (las colecciones caen por el ON DELETE CASCADE).
        var existing = await db.TariffSchedules
            .FirstOrDefaultAsync(t => t.ValidFrom == schedule.ValidFrom, ct);

        if (existing is not null)
            db.TariffSchedules.Remove(existing);

        db.TariffSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        return schedule;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await db.TariffSchedules.Where(t => t.Id == id).ExecuteDeleteAsync(ct) > 0;

    private IQueryable<TariffSchedule> WithDetails()
        => db.TariffSchedules
            .AsNoTracking()
            .Include(t => t.Blocks)
            .Include(t => t.Surcharges)
            .Include(t => t.PeriodCharges);
}

public sealed class BillRepository(EcoWattDbContext db) : IBillRepository
{
    public async Task<IReadOnlyList<ImportedBill>> GetAllAsync(CancellationToken ct = default)
        => await db.ImportedBills.AsNoTracking().OrderByDescending(b => b.ReadingTo).ToListAsync(ct);

    public async Task<ImportedBill?> GetLatestAsync(CancellationToken ct = default)
        => await db.ImportedBills.AsNoTracking().OrderByDescending(b => b.ReadingTo).FirstOrDefaultAsync(ct);

    public async Task<ImportedBill> UpsertAsync(ImportedBill bill, CancellationToken ct = default)
    {
        var existing = await db.ImportedBills
            .FirstOrDefaultAsync(b => b.InvoiceNumber == bill.InvoiceNumber, ct);

        if (existing is not null)
        {
            existing.Period = bill.Period;
            existing.ReadingFrom = bill.ReadingFrom;
            existing.ReadingTo = bill.ReadingTo;
            existing.Days = bill.Days;
            existing.Kwh = bill.Kwh;
            existing.MeterStart = bill.MeterStart;
            existing.MeterEnd = bill.MeterEnd;
            existing.BasicAmount = bill.BasicAmount;
            existing.TotalTaxes = bill.TotalTaxes;
            existing.Total = bill.Total;
            existing.ImportedAt = bill.ImportedAt;

            await db.SaveChangesAsync(ct);
            return existing;
        }

        db.ImportedBills.Add(bill);
        await db.SaveChangesAsync(ct);
        return bill;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await db.ImportedBills.Where(b => b.Id == id).ExecuteDeleteAsync(ct) > 0;
}
