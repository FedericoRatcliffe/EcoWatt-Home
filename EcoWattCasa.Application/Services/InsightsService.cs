using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Domain.Interfaces;

namespace EcoWattCasa.Application.Services;

/// <summary>
/// Lo que se deduce del consumo mas alla del dashboard del periodo: el consumo base, el
/// simulador de ahorro, las facturas contra lo medido y la semana tipica.
///
/// La matematica de cada uno vive en <c>Common/</c> como funcion pura; aca solo se juntan los
/// datos. Lo que ya sabe hacer <see cref="DashboardService"/> (el total de la casa, la
/// proyeccion del ciclo) se le pide a el, para que haya una sola forma de calcularlo.
/// </summary>
public sealed class InsightsService(
    DashboardService dashboard,
    IDeviceRepository devices,
    IEnergyReadingRepository readings,
    ITariffRepository tariffs,
    IBillRepository bills,
    HomeTimeZone tz)
{
    /// <summary>Pisos de las ultimas <paramref name="nights"/> noches ya terminadas.</summary>
    public async Task<(BaseLoadSummary Summary, IReadOnlyList<NightFloor> Nights)> GetBaseLoadSummaryAsync(
        int nights, CancellationToken ct = default)
    {
        var allDevices = await devices.GetAllAsync(ct);

        // Solo noches terminadas: a las 3 de la madrugada el piso "de hoy" todavia puede bajar,
        // y compararlo contra noches enteras daria una suba que no existe.
        var today = tz.Today;
        var nightEnd = tz.StartOfDayUtc(today).AddHours(BaseLoad.NightEndHour);
        var toUtc = DateTimeOffset.UtcNow >= nightEnd ? nightEnd : tz.StartOfDayUtc(today);
        var fromUtc = tz.StartOfDayUtc(today.AddDays(-nights));

        var hourly = await readings.GetHourlySamplesAsync(fromUtc, toUtc, ct);
        var floors = BaseLoad.Nights(hourly, allDevices, tz.Zone);

        return (BaseLoad.Summarize(floors), floors);
    }

    public async Task<BaseLoadDto> GetBaseLoadAsync(int nights, CancellationToken ct = default)
    {
        var allDevices = await devices.GetAllAsync(ct);
        var (summary, floors) = await GetBaseLoadSummaryAsync(nights, ct);
        var period = await dashboard.GetPeriodAsync(useBillingCycle: true, offset: 0, year: null, month: null, ct);
        var price = period.Bill.MarginalPricePerKwh;

        BaseLoadDeviceDto Row(Guid id, string name, double watts, double? last, double? typical, bool unidentified = false)
        {
            var kwh = BaseLoad.CycleKwh(watts);
            return new BaseLoadDeviceDto(
                id,
                name,
                Math.Round(watts, 1),
                Math.Round(kwh, 2),
                Math.Round((decimal)kwh * price, 0),
                last is { } l ? Math.Round(l, 1) : null,
                typical is { } t ? Math.Round(t, 1) : null,
                unidentified);
        }

        var rows = HouseConsumption.Appliances(allDevices)
            .Where(d => summary.DeviceWatts.ContainsKey(d.Id))
            .Select(d => Row(
                d.Id,
                d.Name,
                summary.DeviceWatts[d.Id],
                summary.DeviceLastNightWatts.TryGetValue(d.Id, out var last) ? last : null,
                summary.DeviceTypicalWatts.TryGetValue(d.Id, out var typical) ? typical : null))
            .ToList();

        // Lo que la casa gasta de base y no pasa por ningun enchufe: la heladera, el router,
        // las luces de standby. Con medidor de tablero es la resta; sin el no se puede saber.
        if (period.House.HasMeter && summary.HouseWatts is { } houseWatts)
        {
            var rest = Math.Max(0d, houseWatts - rows.Sum(r => r.Watts));
            rows.Add(Row(Guid.Empty, "Resto de la casa", rest, null, null, unidentified: true));
        }

        var monthlyKwh = summary.HouseWatts is { } w ? BaseLoad.CycleKwh(w) : 0d;
        var cycleKwh = period.Projection?.ProjectedKwh;

        return new BaseLoadDto(
            summary.Nights,
            BaseLoad.NightStartHour,
            BaseLoad.NightEndHour,
            summary.HouseWatts is { } hw ? Math.Round(hw, 1) : null,
            summary.LastNightWatts is { } lw ? Math.Round(lw, 1) : null,
            summary.TypicalWatts is { } tw ? Math.Round(tw, 1) : null,
            Math.Round(monthlyKwh, 2),
            Math.Round((decimal)monthlyKwh * price, 0),
            price,
            cycleKwh is > 0 && monthlyKwh > 0 ? Math.Round(Math.Min(100d, monthlyKwh / cycleKwh.Value * 100d), 1) : null,
            rows.OrderByDescending(r => r.Watts).ToList(),
            floors.Select(f => new BaseLoadNightDto(f.Night, f.HouseWatts is { } v ? Math.Round(v, 1) : null)).ToList());
    }

    public async Task<SimulationDto> SimulateAsync(SimulationRequestDto request, CancellationToken ct = default)
    {
        var period = await dashboard.GetPeriodAsync(useBillingCycle: true, offset: 0, year: null, month: null, ct);
        var history = await tariffs.GetHistoryAsync(ct);
        var cycle = new PeriodWindow(period.From, period.To, period.Label, true);

        var (baselineKwh, source) = request.BaselineKwh is { } given
            ? (given, "el consumo indicado")
            : period.Projection is { } projection
                ? (projection.ProjectedKwh, "la proyeccion del ciclo en curso")
                : (period.Bill.TotalKwh, "lo que va del ciclo en curso");

        var hours = SavingsSimulator.HoursBetween(request.FromHour, request.ToHour);
        var result = SavingsSimulator.Simulate(cycle, baselineKwh, new ConsumptionChange(request.Watts, hours), history);

        return new SimulationDto(
            period.Label,
            source,
            hours,
            Math.Round(result.BaselineKwh, 2),
            Math.Round(result.ScenarioKwh, 2),
            Math.Round(result.DeltaKwh, 2),
            result.Baseline.Total,
            result.Scenario.Total,
            result.Saving,
            Math.Round(result.Saving * 365m / cycle.Days, 0),
            result.Baseline.MarginalPricePerKwh,
            result.Scenario.MarginalPricePerKwh);
    }

    /// <summary>Cada factura importada contra lo que midio el tablero, de la mas nueva a la mas vieja.</summary>
    public async Task<IReadOnlyList<BillCheckDto>> GetBillChecksAsync(CancellationToken ct = default)
    {
        var all = await bills.GetAllAsync(ct);
        var result = new List<BillCheckDto>(all.Count);

        foreach (var bill in all.OrderByDescending(b => b.ReadingTo))
        {
            // Las fechas de lectura son dias: el lector pasa a cualquier hora, asi que se toma
            // de la medianoche del primero a la del ultimo. La tolerancia de BillCheck cubre eso.
            var window = new PeriodWindow(bill.ReadingFrom, bill.ReadingTo, bill.Period, true);
            var (kwh, hours) = await dashboard.GetHouseKwhAsync(window, ct);
            var check = BillCheck.Compare(window, bill.Kwh, kwh, hours);

            result.Add(new BillCheckDto(
                bill.Id,
                bill.Period,
                bill.ReadingFrom,
                bill.ReadingTo,
                check.BilledKwh,
                check.MeasuredKwh,
                check.CoveragePercent,
                check.DifferencePercent,
                check.Status));
        }

        return result;
    }

    /// <summary>La semana tipica de las ultimas <paramref name="weeks"/> semanas, sin contar hoy.</summary>
    public async Task<WeeklyProfileDto> GetWeeklyProfileAsync(int weeks, CancellationToken ct = default)
    {
        var to = tz.Today;
        var from = to.AddDays(-7 * weeks);

        var hourly = await dashboard.GetHouseHourlyAsync(tz.StartOfDayUtc(from), tz.StartOfDayUtc(to), ct);
        var profile = WeeklyProfile.Build(hourly.Select(p => (p.BucketLocal, p.Kwh)));

        return new WeeklyProfileDto(from, to, profile.Cells, profile.DayKwh, profile.WeekdayKwh, profile.WeekendKwh);
    }
}
