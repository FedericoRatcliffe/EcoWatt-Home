using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Application.Billing;

/// <summary>Cuadros deducidos de una factura, con los avisos que haya que mostrar.</summary>
public sealed record TariffScheduleSet(IReadOnlyList<TariffSchedule> Schedules, IReadOnlyList<string> Warnings);

/// <summary>
/// Convierte una factura leida del PDF en los cuadros tarifarios que estuvieron vigentes
/// durante su periodo.
///
/// Una factura puede traer dos juegos de precios: la cooperativa actualiza a fin de mes y la
/// factura reparte los dias del periodo entre el precio viejo y el nuevo. De ese reparto sale
/// la fecha exacta en que empezo a regir cada cuadro.
///
/// Es una funcion pura, sin base ni fecha del sistema, para poder verificarla contra las
/// facturas reales en los tests.
/// </summary>
public static class TariffScheduleFactory
{
    public static TariffScheduleSet Build(ParsedBill parsed, DateTimeOffset createdAt)
    {
        var warnings = new List<string>();
        var schedules = new List<TariffSchedule>();

        if (parsed.Details.Count == 0)
            return new TariffScheduleSet(schedules, warnings);

        var groups = GroupByPricePeriod(parsed.Details);

        // Los dias de los grupos tienen que sumar los dias del periodo medido; si no, las
        // fechas de vigencia que se deducen quedan corridas.
        var totalGroupDays = groups.Sum(g => g.Days);
        if (totalGroupDays != parsed.Days)
        {
            warnings.Add(
                $"Los dias de los tramos suman {totalGroupDays} pero el periodo medido tiene {parsed.Days}. " +
                "Las fechas de vigencia pueden quedar corridas.");
        }

        var surcharges = parsed.Taxes
            .Where(t => t.IsProportional)
            .Select(t => new TariffSurcharge
            {
                Id = Guid.NewGuid(),
                Name = t.Name,
                // La tasa sale del cociente contra el basico; la factura redondea a pesos
                // enteros algunos conceptos, por eso se redondea a 4 decimales.
                Rate = parsed.BasicAmount > 0 ? Math.Round(t.Amount / parsed.BasicAmount, 4) : 0m
            })
            .ToList();

        var periodCharges = parsed.Taxes
            .Where(t => !t.IsProportional)
            .Select(t => new TariffPeriodCharge { Id = Guid.NewGuid(), Name = t.Name, Amount = t.Amount })
            .ToList();

        var start = parsed.ReadingFrom;

        foreach (var (days, lines) in groups)
        {
            var fixedLine = lines.FirstOrDefault(l => l.BlockOrder == 0);
            var blocks = lines
                .Where(l => l.BlockOrder > 0)
                .OrderBy(l => l.BlockOrder)
                .Select(l => new TariffBlock
                {
                    Id = Guid.NewGuid(),
                    Order = l.BlockOrder,
                    Label = $"Hasta {l.UpToKwh:0} kWh",
                    UpToKwh = l.UpToKwh,
                    PricePerKwh = l.UnitPrice
                })
                .ToList();

            if (blocks.Count == 0)
            {
                warnings.Add($"El grupo de {days} dias no trajo tramos variables; se omitio.");
                start = start.AddDays(days);
                continue;
            }

            var schedule = new TariffSchedule
            {
                Id = Guid.NewGuid(),
                ValidFrom = start,
                // El cargo fijo de la factura viene por el total del sub-periodo: se pasa a diario.
                FixedChargePerDay = fixedLine is not null && days > 0
                    ? Math.Round(fixedLine.Amount / days, 4)
                    : 0m,
                Source = $"Factura {parsed.Period} ({parsed.InvoiceNumber})",
                CreatedAt = createdAt
            };

            foreach (var block in blocks)
                schedule.Blocks.Add(block);

            foreach (var surcharge in surcharges)
                schedule.Surcharges.Add(Clone(surcharge));

            foreach (var charge in periodCharges)
                schedule.PeriodCharges.Add(Clone(charge));

            schedules.Add(schedule);
            start = start.AddDays(days);
        }

        return new TariffScheduleSet(schedules, warnings);
    }

    /// <summary>
    /// Agrupa los renglones por cantidad de dias: cada grupo es un periodo de precio. Se
    /// respeta el orden de aparicion, que en la factura es cronologico.
    /// </summary>
    private static List<(int Days, List<BillDetailLine> Lines)> GroupByPricePeriod(IReadOnlyList<BillDetailLine> details)
    {
        var groups = new List<(int Days, List<BillDetailLine> Lines)>();

        foreach (var line in details)
        {
            var index = groups.FindIndex(g => g.Days == line.Days);
            if (index < 0)
            {
                groups.Add((line.Days, [line]));
                continue;
            }

            groups[index].Lines.Add(line);
        }

        return groups;
    }

    private static TariffSurcharge Clone(TariffSurcharge s)
        => new() { Id = Guid.NewGuid(), Name = s.Name, Rate = s.Rate };

    private static TariffPeriodCharge Clone(TariffPeriodCharge c)
        => new() { Id = Guid.NewGuid(), Name = c.Name, Amount = c.Amount };
}
