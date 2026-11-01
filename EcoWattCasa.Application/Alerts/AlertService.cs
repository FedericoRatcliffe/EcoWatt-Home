using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.Services;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
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
    IBillGoalRepository goals,
    DashboardService dashboard,
    InsightsService insights,
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

        // Sin proyeccion (el ciclo ya cerro, o todavia no hay un dia entero medido) se usa lo
        // real: avisa solo si lo consumido ya alcanza, nunca por un ritmo sacado de unas horas.
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
                lastSeen.TryGetValue(d.Id, out var seen) ? seen : null,
                d.Role == DeviceRole.HouseMeter))
            .ToList();

        var goal = await goals.GetAsync(ct);
        var goalProgress = goal is not null && history.Count > 0
            ? GoalPlanner.Evaluate(goal.Amount, cycle, elapsed, period.Bill.TotalKwh, projectedCost, history)
            : null;

        var baseLoad = await BuildBaseLoadShiftAsync(allDevices, period.Bill.MarginalPricePerKwh, ct);

        return new AlertContext(
            cycle,
            elapsed,
            period.Bill.TotalKwh,
            projectedKwh,
            projectedCost,
            period.PreviousPeriodCost ?? 0m,
            ClosingTariff(history, cycle.To),
            statuses,
            goalProgress,
            baseLoad);
    }

    /// <summary>
    /// Anoche contra las noches anteriores. null mientras no haya suficientes noches medidas
    /// para saber que es lo normal.
    /// </summary>
    private async Task<BaseLoadShift?> BuildBaseLoadShiftAsync(
        IReadOnlyList<Device> allDevices, decimal marginalPrice, CancellationToken ct)
    {
        var (summary, _) = await insights.GetBaseLoadSummaryAsync(BaseLoadNights, ct);
        if (summary is not { LastNightWatts: { } last, TypicalWatts: { } typical })
            return null;

        // Solo enchufes: el medidor de tablero es la casa entera, no un sospechoso.
        var names = HouseConsumption.Appliances(allDevices).ToDictionary(d => d.Id, d => d.Name);
        var top = summary.DeviceLastNightWatts
            .Where(kv => summary.DeviceTypicalWatts.ContainsKey(kv.Key) && names.ContainsKey(kv.Key))
            .Select(kv => (Name: names[kv.Key], Rise: kv.Value - summary.DeviceTypicalWatts[kv.Key]))
            .Where(x => x.Rise > 0)
            .OrderByDescending(x => x.Rise)
            .FirstOrDefault();

        return new BaseLoadShift(last, typical, marginalPrice, top.Name, top.Rise);
    }

    /// <summary>Noches contra las que se compara la ultima.</summary>
    private const int BaseLoadNights = 14;

    private static TariffSchedule? ClosingTariff(IReadOnlyList<TariffSchedule> history, DateOnly periodEnd)
        => history
               .Where(s => s.ValidFrom < periodEnd)
               .OrderBy(s => s.ValidFrom)
               .LastOrDefault()
           ?? history.OrderBy(s => s.ValidFrom).LastOrDefault();
}
