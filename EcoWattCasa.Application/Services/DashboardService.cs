using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Domain.ValueObjects;

namespace EcoWattCasa.Application.Services;

/// <summary>
/// Arma las vistas agregadas del dashboard.
///
/// El consumo sale del agregado horario de SQL: los deltas del contador son aditivos, asi que
/// sumar horas contiguas da la energia del periodo, y trabajar por hora acota el dano de un
/// contador reseteado a un solo bucket.
///
/// El costo lo arma <see cref="BillCalculator"/> reconstruyendo la factura entera. No hay un
/// precio del kWh unico: depende del tramo, y los cargos fijos no dependen del consumo.
/// </summary>
public sealed class DashboardService(
    IDeviceRepository devices,
    IEnergyReadingRepository readings,
    ITariffRepository tariffs,
    IBillRepository bills,
    HomeTimeZone tz,
    BillingCycles cycles)
{
    /// <summary>
    /// Fecha en la que se ancla el ciclo: la ultima lectura real del medidor, que viene de la
    /// factura importada mas reciente. Sin facturas, se supone el dia 2.
    /// </summary>
    public async Task<(DateOnly Anchor, bool FromBill)> GetCycleAnchorAsync(CancellationToken ct = default)
    {
        var latest = await bills.GetLatestAsync(ct);
        return latest is not null ? (latest.ReadingTo, true) : (cycles.DefaultAnchor(), false);
    }

    public async Task<DailyDashboardDto> GetDailyAsync(DateOnly date, CancellationToken ct = default)
    {
        var allDevices = await devices.GetAllAsync(ct);
        var history = await tariffs.GetHistoryAsync(ct);
        var (anchor, _) = await GetCycleAnchorAsync(ct);

        // El costo de un dia depende de en que parte del ciclo cae: los kWh del final del
        // ciclo pegan en el tramo caro. Asi que se costea el ciclo entero y se mira el dia.
        var cycle = cycles.CycleContaining(date, anchor);
        var (cycleFromUtc, cycleToUtc) = cycles.ToUtc(cycle);
        var cycleHourly = await readings.GetHourlySamplesAsync(cycleFromUtc, cycleToUtc, ct);

        var cycleTotals = new Dictionary<Guid, DeviceTotals>();
        var cycleDays = FoldBuckets(cycleHourly, allDevices, tz.Zone, BucketGranularity.Day, cycleTotals);
        var cycleKwh = cycleDays.Sum(p => p.Kwh);

        var bill = BillCalculator.Build(cycle.From, cycle.To, cycleKwh, history);
        var closing = ClosingSchedule(history, cycle.To);

        // Reparto cronologico: cada dia del ciclo se cobra al tramo que le toco.
        var dayCosts = BillCalculator.AllocateChronologically(bill, cycleDays.Select(d => d.Kwh).ToList(), closing);
        var dayIndex = cycleDays.FindIndex(p => DateOnly.FromDateTime(p.BucketLocal.DateTime) == date);
        var dayCost = dayIndex >= 0 ? dayCosts[dayIndex] : 0m;
        var dayKwh = dayIndex >= 0 ? cycleDays[dayIndex].Kwh : 0d;

        // Precio efectivo del dia, para repartirlo entre horas y dispositivos.
        var dayPrice = dayKwh > 0 ? dayCost / (decimal)dayKwh : bill.MarginalPricePerKwh;

        var (fromUtc, toUtc) = tz.DayWindow(date);
        var hourly = await readings.GetHourlySamplesAsync(fromUtc, toUtc, ct);
        var perDevice = new Dictionary<Guid, DeviceTotals>();
        var hourlyPoints = FoldBuckets(hourly, allDevices, tz.Zone, BucketGranularity.Hour, perDevice);

        var pricedHours = hourlyPoints
            .Select(p => p with { Cost = Math.Round((decimal)p.Kwh * dayPrice, 2) })
            .ToList();

        var latestReadings = await readings.GetLatestPerDeviceAsync(ct);
        var currentWatts = BuildCurrentWatts(latestReadings);

        var consumptions = allDevices
            .Select(d =>
            {
                var kwh = perDevice.TryGetValue(d.Id, out var t) ? t.Kwh : 0d;
                return new DeviceConsumptionDto(
                    d.Id,
                    d.Name,
                    d.Location,
                    Math.Round(kwh, 4),
                    Math.Round((decimal)kwh * dayPrice, 2),
                    currentWatts.TryGetValue(d.Id, out var w) ? w : null,
                    0d);
            })
            .ToList();

        return new DailyDashboardDto(
            date,
            Math.Round(dayKwh, 4),
            dayCost,
            bill.MarginalPricePerKwh,
            WithShares(consumptions, dayKwh),
            pricedHours);
    }

    /// <summary>
    /// Dashboard del periodo. <paramref name="useBillingCycle"/> elige entre el ciclo real de
    /// la cooperativa y el mes calendario; <paramref name="offset"/> se mueve hacia atras.
    /// </summary>
    public async Task<PeriodDashboardDto> GetPeriodAsync(
        bool useBillingCycle, int offset, int? year, int? month, CancellationToken ct = default)
    {
        var (anchor, _) = await GetCycleAnchorAsync(ct);
        var window = ResolveWindow(useBillingCycle, offset, year, month, anchor);
        var previous = PreviousWindow(window, useBillingCycle);

        var allDevices = await devices.GetAllAsync(ct);
        var history = await tariffs.GetHistoryAsync(ct);
        var closing = ClosingSchedule(history, window.To);

        var perDevice = new Dictionary<Guid, DeviceTotals>();
        var dailyPoints = await BuildDailyAsync(window, allDevices, perDevice, ct);
        var totalKwh = dailyPoints.Sum(p => p.Kwh);

        var bill = BillCalculator.Build(window.From, window.To, totalKwh, history);
        var dayCosts = BillCalculator.AllocateChronologically(bill, dailyPoints.Select(p => p.Kwh).ToList(), closing);

        var pricedDays = dailyPoints
            .Select((p, i) => p with { Cost = dayCosts[i] })
            .ToList();

        var latestReadings = await readings.GetLatestPerDeviceAsync(ct);
        var currentWatts = BuildCurrentWatts(latestReadings);

        var consumptions = allDevices
            .Select(d =>
            {
                var kwh = perDevice.TryGetValue(d.Id, out var t) ? t.Kwh : 0d;
                return new DeviceConsumptionDto(
                    d.Id,
                    d.Name,
                    d.Location,
                    Math.Round(kwh, 4),
                    BillCalculator.AllocateToDevice(bill, kwh),
                    currentWatts.TryGetValue(d.Id, out var w) ? w : null,
                    0d);
            })
            .ToList();

        // Comparacion honesta: mientras el periodo esta abierto se compara contra el anterior
        // recortado a la misma cantidad de dias. Medio ciclo contra uno entero siempre da
        // "bajaste un 40%", que no dice nada.
        var elapsed = window.Contains(tz.Today) ? window.ElapsedDays(tz.Today) : window.Days;
        var isPartial = elapsed < window.Days;

        var previousBill = await BuildBillAsync(previous, allDevices, history, ct);
        var comparisonBill = isPartial
            ? await BuildBillAsync(Truncate(previous, elapsed), allDevices, history, ct)
            : previousBill;

        double? change = comparisonBill.Total > 0m
            ? Math.Round((double)((bill.Total - comparisonBill.Total) / comparisonBill.Total) * 100d, 2)
            : null;

        return new PeriodDashboardDto(
            window.Label,
            window.From,
            window.To,
            window.IsBillingCycle,
            MapBill(bill),
            previousBill.Total,
            comparisonBill.Total,
            isPartial,
            change,
            BuildProjection(window, totalKwh, elapsed, history),
            WithShares(consumptions, totalKwh),
            pricedDays);
    }

    public async Task<DeviceHistoryDto?> GetDeviceHistoryAsync(
        Guid deviceId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        BucketGranularity granularity,
        CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct);
        if (device is null)
            return null;

        var history = await tariffs.GetHistoryAsync(ct);
        var hourly = await readings.GetHourlySamplesAsync(fromUtc, toUtc, ct);
        var onlyThisDevice = hourly.Where(b => b.DeviceId == deviceId).ToList();

        var points = FoldBuckets(onlyThisDevice, [device], tz.Zone, granularity, null);

        // Para un dispositivo suelto se usa el precio marginal del ciclo en curso: su consumo
        // se apila sobre el del resto de la casa, asi que lo que cuesta (o lo que se ahorra
        // apagandolo) es el precio del tramo en el que esta parada la casa ahora mismo.
        var marginal = await CurrentMarginalPriceAsync(history, ct);

        var priced = points
            .Select(p => p with { Cost = Math.Round((decimal)p.Kwh * marginal, 2) })
            .ToList();

        return new DeviceHistoryDto(
            device.Id,
            device.Name,
            fromUtc,
            toUtc,
            Math.Round(priced.Sum(p => p.Kwh), 4),
            Math.Round(priced.Sum(p => p.Cost), 2),
            priced);
    }

    // ---------- helpers ----------

    private PeriodWindow ResolveWindow(bool useBillingCycle, int offset, int? year, int? month, DateOnly anchor)
    {
        if (!useBillingCycle)
        {
            var today = tz.Today;
            return cycles.CalendarMonth(year ?? today.Year, month ?? today.Month);
        }

        return cycles.Cycle(offset, anchor);
    }

    private PeriodWindow PreviousWindow(PeriodWindow window, bool useBillingCycle)
    {
        if (!useBillingCycle)
        {
            var previousMonth = window.From.AddMonths(-1);
            return cycles.CalendarMonth(previousMonth.Year, previousMonth.Month);
        }

        var from = window.From.AddDays(-BillingCycles.NominalCycleDays);
        return new PeriodWindow(from, window.From, $"Ciclo {from:dd/MM} - {window.From.AddDays(-1):dd/MM}", true);
    }

    private async Task<List<HistoryPointDto>> BuildDailyAsync(
        PeriodWindow window, IReadOnlyList<Device> allDevices, Dictionary<Guid, DeviceTotals> perDevice, CancellationToken ct)
    {
        var (fromUtc, toUtc) = cycles.ToUtc(window);
        var hourly = await readings.GetHourlySamplesAsync(fromUtc, toUtc, ct);
        return FoldBuckets(hourly, allDevices, tz.Zone, BucketGranularity.Day, perDevice);
    }

    private async Task<BillBreakdown> BuildBillAsync(
        PeriodWindow window, IReadOnlyList<Device> allDevices, IReadOnlyList<TariffSchedule> history, CancellationToken ct)
    {
        var points = await BuildDailyAsync(window, allDevices, new Dictionary<Guid, DeviceTotals>(), ct);
        return BillCalculator.Build(window.From, window.To, points.Sum(p => p.Kwh), history);
    }

    private ProjectionDto? BuildProjection(
        PeriodWindow window, double totalKwh, int elapsed, IReadOnlyList<TariffSchedule> history)
    {
        if (!window.Contains(tz.Today) || elapsed <= 0 || elapsed >= window.Days)
            return null;

        var projectedKwh = totalKwh / elapsed * window.Days;
        var projectedBill = BillCalculator.Build(window.From, window.To, projectedKwh, history);

        return new ProjectionDto(
            elapsed,
            window.Days,
            Math.Round(projectedKwh, 2),
            projectedBill.Total,
            projectedBill.AveragePricePerKwh);
    }

    /// <summary>Recorta una ventana a sus primeros <paramref name="days"/> dias.</summary>
    private static PeriodWindow Truncate(PeriodWindow window, int days)
        => window with { To = window.From.AddDays(Math.Clamp(days, 1, window.Days)) };

    private static TariffSchedule? ClosingSchedule(IReadOnlyList<TariffSchedule> history, DateOnly periodEnd)
        => history
               .Where(s => s.ValidFrom < periodEnd)
               .OrderBy(s => s.ValidFrom)
               .LastOrDefault()
           ?? history.OrderBy(s => s.ValidFrom).FirstOrDefault();

    /// <summary>
    /// Precio del proximo kWh: depende de cuanto lleva consumido la casa en el ciclo en curso,
    /// porque el tramo se define sobre el acumulado del periodo.
    /// </summary>
    private async Task<decimal> CurrentMarginalPriceAsync(IReadOnlyList<TariffSchedule> history, CancellationToken ct)
    {
        var (anchor, _) = await GetCycleAnchorAsync(ct);
        var cycle = cycles.CycleContaining(tz.Today, anchor);
        var (fromUtc, toUtc) = cycles.ToUtc(cycle);

        var allDevices = await devices.GetAllAsync(ct);
        var hourly = await readings.GetHourlySamplesAsync(fromUtc, toUtc, ct);
        var days = FoldBuckets(hourly, allDevices, tz.Zone, BucketGranularity.Day, null);

        return BillCalculator.Build(cycle.From, cycle.To, days.Sum(p => p.Kwh), history).MarginalPricePerKwh;
    }

    private static BillDto MapBill(BillBreakdown bill)
        => new(
            bill.Days,
            bill.TotalKwh,
            bill.FixedCharge,
            bill.VariableCharge,
            bill.BasicAmount,
            bill.Blocks.Select(b => new BillBlockDto(b.Label, b.Kwh, b.PricePerKwh, b.Amount)).ToList(),
            bill.Surcharges.Select(s => new BillChargeDto(s.Name, s.Rate, s.Amount)).ToList(),
            bill.PeriodCharges.Select(c => new BillChargeDto(c.Name, null, c.Amount)).ToList(),
            bill.TotalSurcharges,
            bill.Total,
            bill.MarginalPricePerKwh,
            bill.AveragePricePerKwh,
            bill.EnergyCostWithTaxes,
            bill.FixedCostWithTaxes);

    private static Dictionary<Guid, double> BuildCurrentWatts(IReadOnlyList<EnergyReading> latest)
        => latest
            .GroupBy(r => r.DeviceId)
            .ToDictionary(g => g.Key, g => Math.Round(g.First().Watts, 2));

    private static List<DeviceConsumptionDto> WithShares(List<DeviceConsumptionDto> items, double totalKwh)
        => items
            .Select(c => c with { SharePercent = totalKwh > 0 ? Math.Round(c.Kwh / totalKwh * 100d, 2) : 0d })
            .OrderByDescending(c => c.Cost)
            .ThenBy(c => c.Name)
            .ToList();

    /// <summary>
    /// Los buckets llegan en hora UTC desde SQL. Aca se convierten a hora local y se agrupan
    /// por hora o por dia, acumulando de paso el consumo de cada dispositivo. El costo se
    /// completa despues, cuando ya se sabe en que tramo de la factura cae cada bucket.
    /// </summary>
    private static List<HistoryPointDto> FoldBuckets(
        IReadOnlyList<BucketEnergySamples> buckets,
        IReadOnlyList<Device> devices,
        TimeZoneInfo zone,
        BucketGranularity granularity,
        Dictionary<Guid, DeviceTotals>? perDeviceTotals)
    {
        var cumulativeByDevice = devices.ToDictionary(d => d.Id, d => d.ReportsCumulativeEnergy);
        var folded = new Dictionary<DateTimeOffset, FoldedBucket>();
        var bucketDuration = TimeSpan.FromHours(1);

        foreach (var bucket in buckets)
        {
            var local = TimeZoneInfo.ConvertTime(bucket.BucketStartUtc, zone);
            var key = granularity == BucketGranularity.Hour
                ? new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset)
                : new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);

            var prefersCumulative = !cumulativeByDevice.TryGetValue(bucket.DeviceId, out var pc) || pc;
            var kwh = EnergyMath.ToKwh(bucket.Samples, prefersCumulative, bucketDuration);

            if (!folded.TryGetValue(key, out var acc))
                folded[key] = acc = new FoldedBucket();

            acc.Kwh += kwh;

            // Potencia media de la casa: se suman las medias de cada dispositivo y se divide
            // por las horas distintas que entraron al bucket.
            acc.WattsSum += bucket.Samples.AvgWatts;
            acc.SourceHours.Add(bucket.BucketStartUtc);
            acc.MaxWatts = Math.Max(acc.MaxWatts, bucket.Samples.MaxWatts);

            if (perDeviceTotals is not null)
            {
                if (!perDeviceTotals.TryGetValue(bucket.DeviceId, out var totals))
                    perDeviceTotals[bucket.DeviceId] = totals = new DeviceTotals();

                totals.Kwh += kwh;
            }
        }

        return folded
            .OrderBy(kv => kv.Key)
            .Select(kv => new HistoryPointDto(
                kv.Key,
                Math.Round(kv.Value.Kwh, 4),
                0m,
                kv.Value.SourceHours.Count > 0 ? Math.Round(kv.Value.WattsSum / kv.Value.SourceHours.Count, 2) : 0d,
                Math.Round(kv.Value.MaxWatts, 2)))
            .ToList();
    }

    private sealed class FoldedBucket
    {
        public double Kwh;
        public double WattsSum;
        public readonly HashSet<DateTimeOffset> SourceHours = [];
        public double MaxWatts;
    }
}

/// <summary>Granularidad de los puntos que se devuelven al frontend.</summary>
public enum BucketGranularity
{
    Hour,
    Day
}
