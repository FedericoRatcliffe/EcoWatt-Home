using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;

namespace EcoWattCasa.Application.Services;

public sealed class TariffService(ITariffRepository tariffs, IBillRepository bills, HomeTimeZone tz)
{
    public async Task<TariffScheduleDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var schedule = await tariffs.GetCurrentAsync(ct);
        return schedule is null ? null : Map(schedule);
    }

    public async Task<IReadOnlyList<TariffScheduleDto>> GetHistoryAsync(CancellationToken ct = default)
    {
        var history = await tariffs.GetHistoryAsync(ct);
        return history.OrderByDescending(s => s.ValidFrom).Select(Map).ToList();
    }

    /// <summary>
    /// Guarda un cuadro nuevo. No pisa los anteriores: cada uno tiene su vigencia, asi los
    /// periodos ya costeados no cambian de precio de forma retroactiva.
    /// </summary>
    public async Task<TariffScheduleDto> UpsertAsync(UpsertTariffDto dto, CancellationToken ct = default)
    {
        if (dto.Blocks.Count == 0)
            throw new DeviceValidationException("La tarifa necesita al menos un tramo de consumo.");

        var schedule = new TariffSchedule
        {
            Id = Guid.NewGuid(),
            ValidFrom = dto.ValidFrom ?? tz.Today,
            FixedChargePerDay = dto.FixedChargePerDay,
            Source = string.IsNullOrWhiteSpace(dto.Source) ? "Carga manual" : dto.Source.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        foreach (var block in dto.Blocks.OrderBy(b => b.Order))
        {
            schedule.Blocks.Add(new TariffBlock
            {
                Id = Guid.NewGuid(),
                Order = block.Order,
                Label = string.IsNullOrWhiteSpace(block.Label) ? $"Tramo {block.Order}" : block.Label.Trim(),
                UpToKwh = block.UpToKwh,
                PricePerKwh = block.PricePerKwh
            });
        }

        foreach (var surcharge in dto.Surcharges)
        {
            schedule.Surcharges.Add(new TariffSurcharge
            {
                Id = Guid.NewGuid(),
                Name = surcharge.Name.Trim(),
                Rate = surcharge.Rate
            });
        }

        foreach (var charge in dto.PeriodCharges)
        {
            schedule.PeriodCharges.Add(new TariffPeriodCharge
            {
                Id = Guid.NewGuid(),
                Name = charge.Name.Trim(),
                Amount = charge.Amount
            });
        }

        var saved = await tariffs.UpsertAsync(schedule, ct);
        return Map(saved);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => tariffs.DeleteAsync(id, ct);

    public async Task<IReadOnlyList<ImportedBillDto>> GetBillsAsync(CancellationToken ct = default)
    {
        var all = await bills.GetAllAsync(ct);
        return all.OrderByDescending(b => b.ReadingTo).Select(MapBill).ToList();
    }

    public Task<bool> DeleteBillAsync(Guid id, CancellationToken ct = default) => bills.DeleteAsync(id, ct);

    public static TariffScheduleDto Map(TariffSchedule s)
    {
        var top = s.Blocks.OrderBy(b => b.Order).LastOrDefault();

        return new TariffScheduleDto(
            s.Id,
            s.ValidFrom,
            s.FixedChargePerDay,
            s.Source,
            s.CreatedAt,
            s.Blocks.OrderBy(b => b.Order)
                .Select(b => new TariffBlockDto(b.Order, b.Label, b.UpToKwh, b.PricePerKwh))
                .ToList(),
            s.Surcharges.OrderByDescending(x => x.Rate)
                .Select(x => new TariffSurchargeDto(x.Name, x.Rate))
                .ToList(),
            s.PeriodCharges.OrderByDescending(x => x.Amount)
                .Select(x => new TariffPeriodChargeDto(x.Name, x.Amount))
                .ToList(),
            s.TotalSurchargeRate,
            Math.Round((top?.PricePerKwh ?? 0m) * (1m + s.TotalSurchargeRate), 2));
    }

    public static ImportedBillDto MapBill(ImportedBill b)
        => new(
            b.Id,
            b.InvoiceNumber,
            b.Period,
            b.ReadingFrom,
            b.ReadingTo,
            b.Days,
            b.Kwh,
            b.BasicAmount,
            b.TotalTaxes,
            b.Total,
            b.AveragePricePerKwh,
            b.ImportedAt);
}
