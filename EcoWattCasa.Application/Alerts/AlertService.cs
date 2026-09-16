using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.Services;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace EcoWattCasa.Application.Alerts;

/// <summary>
/// Junta el estado del ciclo en curso y se lo pasa a <see cref="AlertRules"/>.
///
/// Las alertas se calculan cuando se piden, no se guardan: son una lectura del estado actual,
/// no un historial. Eso evita tener que resolver acuses de recibo y alertas rancias, y alcanza
/// para un dashboard que uno mira. Si en algun momento se quieren notificaciones por mail o
/// push, ahi si hace falta persistirlas.
/// </summary>
public sealed class AlertService(
    IDeviceRepository devices,
    IEnergyReadingRepository readings,
    ITariffRepository tariffs,
    DashboardService dashboard,
    HomeTimeZone tz,
    BillingCycles cycles,
    IOptions<AlertThresholds> thresholds)
{
    private readonly AlertThresholds _thresholds = thresholds.Value;

    public async Task<IReadOnlyList<Alert>> GetActiveAsync(CancellationToken ct = default)
    {
        if (!_thresholds.Enabled)
            return [];

        var context = await BuildContextAsync(ct);
        return AlertRules.Evaluate(context, _thresholds, DateTimeOffset.UtcNow);
    }

    private async Task<AlertContext> BuildContextAsync(CancellationToken ct)
    {
        var (anchor, _) = await dashboard.GetCycleAnchorAsync(ct);
        var cycle = cycles.CycleContaining(tz.Today, anchor);
        var elapsed = cycle.ElapsedDays(tz.Today);

        var history = await tariffs.GetHistoryAsync(ct);
        var period = await dashboard.GetPeriodAsync(useBillingCycle: true, offset: 0, year: null, month: null, ct);

        // La proyeccion solo existe con el ciclo abierto; cerrado, lo proyectado es lo real.
        var projectedKwh = period.Projection?.ProjectedKwh ?? period.Bill.TotalKwh;
        var projectedCost = period.Projection?.ProjectedCost ?? period.Bill.Total;

        var allDevices = await devices.GetAllAsync(ct);
        var latest = await readings.GetLatestPerDeviceAsync(ct);
        var lastSeen = latest
            .GroupBy(r => r.DeviceId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Timestamp));

        var statuses = allDevices
            .Select(d => new DeviceStatus(
                d.Id,
                d.Name,
                d.IsActive,
                lastSeen.TryGetValue(d.Id, out var seen) ? seen : null))
            .ToList();

        return new AlertContext(
            cycle,
            elapsed,
            period.Bill.TotalKwh,
            projectedKwh,
            projectedCost,
            period.PreviousPeriodCost,
            ClosingTariff(history, cycle.To),
            statuses);
    }

    private static TariffSchedule? ClosingTariff(IReadOnlyList<TariffSchedule> history, DateOnly periodEnd)
        => history
               .Where(s => s.ValidFrom < periodEnd)
               .OrderBy(s => s.ValidFrom)
               .LastOrDefault()
           ?? history.OrderBy(s => s.ValidFrom).LastOrDefault();
}
