using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Application.Common;

/// <summary>Una linea de tramo del cargo variable.</summary>
public sealed record BlockLine(string Label, double Kwh, decimal PricePerKwh, decimal Amount);

/// <summary>Un recargo porcentual ya aplicado sobre el importe basico.</summary>
public sealed record SurchargeLine(string Name, decimal Rate, decimal Amount);

/// <summary>Un cargo de monto fijo del periodo.</summary>
public sealed record FixedChargeLine(string Name, decimal Amount);

/// <summary>
/// Factura reconstruida para un periodo. <see cref="Total"/> es lo que deberia venir en el
/// papel: cargo fijo + tramos + recargos porcentuales + cargos fijos del periodo.
/// </summary>
public sealed record BillBreakdown(
    DateOnly From,
    DateOnly To,
    int Days,
    double TotalKwh,
    decimal FixedCharge,
    decimal VariableCharge,
    decimal BasicAmount,
    IReadOnlyList<BlockLine> Blocks,
    IReadOnlyList<SurchargeLine> Surcharges,
    IReadOnlyList<FixedChargeLine> PeriodCharges,
    decimal TotalSurcharges,
    decimal Total,
    decimal MarginalPricePerKwh,
    decimal AveragePricePerKwh,
    decimal EnergyCostWithTaxes,
    decimal FixedCostWithTaxes)
{
    /// <summary>Vacia, para cuando no hay ninguna tarifa cargada todavia.</summary>
    public static BillBreakdown Empty(DateOnly from, DateOnly to)
        => new(from, to, 0, 0, 0m, 0m, 0m, [], [], [], 0m, 0m, 0m, 0m, 0m, 0m);
}

/// <summary>
/// Reconstruye la factura de un periodo a partir del consumo medido y de la serie de
/// cuadros tarifarios.
///
/// Reglas tomadas de las facturas reales de la cooperativa de Venado Tuerto:
/// - El cargo fijo es diario y se prorratea por los dias de cada cuadro vigente.
/// - Los topes de los tramos (75 / 150 / 300 kWh) son por periodo facturado, no por dia.
///   Cuando el precio cambia a mitad de periodo, los kWh de cada tramo se reparten entre
///   los cuadros en proporcion a los dias, que es exactamente lo que hace la factura.
/// - Los recargos porcentuales y los cargos fijos del periodo se toman del cuadro vigente
///   al cierre del periodo.
/// </summary>
public static class BillCalculator
{
    public static BillBreakdown Build(
        DateOnly from,
        DateOnly to,
        double totalKwh,
        IReadOnlyList<TariffSchedule> history)
    {
        var days = Math.Max(0, to.DayNumber - from.DayNumber);
        if (history.Count == 0 || days == 0)
            return BillBreakdown.Empty(from, to);

        // Dias que le tocan a cada cuadro dentro del periodo.
        var daysBySchedule = SplitDays(from, to, history);
        if (daysBySchedule.Count == 0)
            return BillBreakdown.Empty(from, to);

        var closing = daysBySchedule[^1].Schedule;

        var fixedCharge = daysBySchedule.Sum(d => d.Days * d.Schedule.FixedChargePerDay);
        var (blocks, variableCharge) = BuildBlocks(totalKwh, daysBySchedule, closing, days);

        var basicAmount = Round(fixedCharge + variableCharge);

        var surcharges = closing.Surcharges
            .OrderByDescending(s => s.Rate)
            .Select(s => new SurchargeLine(s.Name, s.Rate, Round(basicAmount * s.Rate)))
            .ToList();

        var periodCharges = closing.PeriodCharges
            .OrderByDescending(c => c.Amount)
            .Select(c => new FixedChargeLine(c.Name, Round(c.Amount)))
            .ToList();

        var totalSurcharges = surcharges.Sum(s => s.Amount);
        var total = Round(basicAmount + totalSurcharges + periodCharges.Sum(c => c.Amount));

        // El costo se parte en dos: lo que depende del consumo (y por lo tanto se puede
        // repartir entre los aparatos) y lo que se paga aunque no se consuma nada.
        var surchargeRate = closing.TotalSurchargeRate;
        var energyCost = Round(variableCharge * (1m + surchargeRate));
        var fixedCost = Round(total - energyCost);

        return new BillBreakdown(
            from,
            to,
            days,
            Math.Round(totalKwh, 4),
            Round(fixedCharge),
            Round(variableCharge),
            basicAmount,
            blocks,
            surcharges,
            periodCharges,
            totalSurcharges,
            total,
            Round(closing.PriceAt(totalKwh) * (1m + surchargeRate)),
            totalKwh > 0 ? Round(total / (decimal)totalKwh) : 0m,
            energyCost,
            fixedCost);
    }

    /// <summary>
    /// Costo que se le imputa a un dispositivo: su parte del costo de energia, en proporcion
    /// a los kWh que consumio. Los cargos fijos no se reparten, no son de ningun aparato.
    /// </summary>
    public static decimal AllocateToDevice(BillBreakdown bill, double deviceKwh)
        => bill.TotalKwh > 0
            ? Round(bill.EnergyCostWithTaxes * (decimal)(deviceKwh / bill.TotalKwh))
            : 0m;

    /// <summary>
    /// Reparte el costo de energia entre buckets consecutivos (dias u horas) respetando los
    /// tramos: los primeros kWh del periodo caen en el tramo barato y los ultimos en el caro.
    /// La suma de lo devuelto es el costo de energia del periodo, no una aproximacion.
    /// </summary>
    public static IReadOnlyList<decimal> AllocateChronologically(
        BillBreakdown bill, IReadOnlyList<double> bucketKwh, TariffSchedule? closing)
    {
        var costs = new decimal[bucketKwh.Count];
        if (bill.TotalKwh <= 0 || closing is null)
            return costs;

        // Se recorre el periodo acumulando kWh y cobrando cada tramo a su precio. El precio
        // usado es el ponderado que ya calculo BuildBlocks, asi cierra con el total.
        var priceByOrder = bill.Blocks
            .Select((line, index) => (line, index))
            .ToDictionary(x => x.index, x => x.line.PricePerKwh);

        var bounds = closing.Blocks
            .OrderBy(b => b.Order)
            .Select(b => b.UpToKwh ?? double.MaxValue)
            .ToList();

        var surcharge = 1m + closing.TotalSurchargeRate;
        var consumed = 0d;

        for (var i = 0; i < bucketKwh.Count; i++)
        {
            var remaining = bucketKwh[i];
            var amount = 0m;

            while (remaining > 0)
            {
                var blockIndex = BlockIndexAt(bounds, consumed);
                var roomLeft = bounds[blockIndex] == double.MaxValue
                    ? remaining
                    : Math.Max(0d, bounds[blockIndex] - consumed);

                var take = roomLeft > 0 ? Math.Min(remaining, roomLeft) : remaining;
                var price = priceByOrder.TryGetValue(blockIndex, out var p) ? p : bill.AveragePricePerKwh;

                amount += (decimal)take * price * surcharge;
                consumed += take;
                remaining -= take;
            }

            costs[i] = Round(amount);
        }

        return costs;
    }

    private static int BlockIndexAt(List<double> bounds, double consumed)
    {
        for (var i = 0; i < bounds.Count; i++)
        {
            if (consumed < bounds[i])
                return i;
        }

        return Math.Max(0, bounds.Count - 1);
    }

    private sealed record ScheduleDays(TariffSchedule Schedule, int Days);

    /// <summary>Cuantos dias del periodo cae bajo cada cuadro tarifario.</summary>
    private static List<ScheduleDays> SplitDays(DateOnly from, DateOnly to, IReadOnlyList<TariffSchedule> history)
    {
        var ordered = history.OrderBy(s => s.ValidFrom).ToList();
        var counts = new Dictionary<Guid, (TariffSchedule Schedule, int Days)>();
        var order = new List<Guid>();

        for (var day = from; day < to; day = day.AddDays(1))
        {
            var schedule = ScheduleFor(ordered, day);
            if (schedule is null)
                continue;

            if (!counts.TryGetValue(schedule.Id, out var entry))
            {
                order.Add(schedule.Id);
                entry = (schedule, 0);
            }

            counts[schedule.Id] = (schedule, entry.Days + 1);
        }

        return order.Select(id => new ScheduleDays(counts[id].Schedule, counts[id].Days)).ToList();
    }

    private static TariffSchedule? ScheduleFor(List<TariffSchedule> ordered, DateOnly day)
    {
        TariffSchedule? match = null;
        foreach (var schedule in ordered)
        {
            if (schedule.ValidFrom <= day)
                match = schedule;
            else
                break;
        }

        // Un dia anterior al primer cuadro cargado se costea con el mas viejo que haya.
        return match ?? ordered.FirstOrDefault();
    }

    /// <summary>
    /// Parte el consumo en tramos y, dentro de cada tramo, entre los cuadros vigentes
    /// segun los dias, como hace la factura.
    /// </summary>
    private static (List<BlockLine> Lines, decimal Total) BuildBlocks(
        double totalKwh,
        List<ScheduleDays> daysBySchedule,
        TariffSchedule closing,
        int totalDays)
    {
        var lines = new List<BlockLine>();
        var total = 0m;

        var definitions = closing.Blocks.OrderBy(b => b.Order).ToList();
        var consumed = 0d;

        foreach (var definition in definitions)
        {
            if (consumed >= totalKwh)
                break;

            var top = definition.UpToKwh ?? double.MaxValue;
            var kwhInBlock = Math.Min(totalKwh, top) - consumed;
            if (kwhInBlock <= 0)
                continue;

            consumed += kwhInBlock;

            // Reparto del tramo entre los cuadros, proporcional a los dias.
            var amount = 0m;
            var assigned = 0d;

            for (var i = 0; i < daysBySchedule.Count; i++)
            {
                var (schedule, days) = (daysBySchedule[i].Schedule, daysBySchedule[i].Days);

                // La factura reparte los tramos en kWh enteros (el medidor lee enteros), y el
                // ultimo sub-periodo se lleva el resto. Replicarlo hace que el total recalculado
                // coincida al centavo con el papel.
                var share = i == daysBySchedule.Count - 1
                    ? kwhInBlock - assigned
                    : Math.Round(kwhInBlock * days / totalDays, 0, MidpointRounding.AwayFromZero);

                assigned += share;

                var price = PriceOfBlock(schedule, definition.Order, definition.PricePerKwh);
                amount += (decimal)share * price;
            }

            var weightedPrice = kwhInBlock > 0 ? Round(amount / (decimal)kwhInBlock) : 0m;
            lines.Add(new BlockLine(definition.Label, Math.Round(kwhInBlock, 4), weightedPrice, Round(amount)));
            total += amount;
        }

        return (lines, total);
    }

    /// <summary>Precio del tramo N en un cuadro dado; si ese cuadro no lo define, usa el de referencia.</summary>
    private static decimal PriceOfBlock(TariffSchedule schedule, int order, decimal fallback)
        => schedule.Blocks.FirstOrDefault(b => b.Order == order)?.PricePerKwh ?? fallback;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
