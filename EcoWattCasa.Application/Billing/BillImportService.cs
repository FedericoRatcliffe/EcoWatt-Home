using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Services;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EcoWattCasa.Application.Billing;

/// <summary>Extrae el texto de un PDF. Lo implementa Infrastructure.</summary>
public interface IBillTextExtractor
{
    Task<string> ExtractTextAsync(Stream pdf, CancellationToken ct = default);
}

/// <summary>
/// Importa una factura en PDF: guarda el comprobante y deduce los cuadros tarifarios que
/// estuvieron vigentes durante el periodo.
///
/// Una factura puede traer dos juegos de precios (la cooperativa actualiza a fin de mes y la
/// factura reparte los dias). Los renglones de "DETALLE TARIFAS APLICADAS" vienen agrupados
/// por cantidad de dias, y de ahi sale la fecha exacta en que empezo a regir cada precio.
/// </summary>
public sealed class BillImportService(
    ITariffRepository tariffs,
    IBillRepository bills,
    IBillTextExtractor extractor,
    ILogger<BillImportService> logger)
{
    public async Task<BillImportResultDto> ImportAsync(Stream pdf, CancellationToken ct = default)
    {
        var text = await extractor.ExtractTextAsync(pdf, ct);
        var parsed = BillParser.Parse(text);

        logger.LogInformation(
            "Factura {Invoice} periodo {Period}: {Kwh} kWh entre {From} y {To}, total {Total}",
            parsed.InvoiceNumber, parsed.Period, parsed.Kwh, parsed.ReadingFrom, parsed.ReadingTo, parsed.Total);

        var warnings = new List<string>(parsed.Warnings);

        var bill = await bills.UpsertAsync(new ImportedBill
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = parsed.InvoiceNumber,
            Period = parsed.Period,
            ReadingFrom = parsed.ReadingFrom,
            ReadingTo = parsed.ReadingTo,
            Days = parsed.Days,
            Kwh = parsed.Kwh,
            MeterStart = parsed.MeterStart,
            MeterEnd = parsed.MeterEnd,
            BasicAmount = parsed.BasicAmount,
            TotalTaxes = parsed.TotalTaxes,
            Total = parsed.Total,
            ImportedAt = DateTimeOffset.UtcNow
        }, ct);

        var schedules = await SaveSchedulesAsync(parsed, warnings, ct);
        await CompactAsync(ct);

        // Control de calidad: se vuelve a armar la factura con lo que se guardo y se compara
        // contra el total impreso. Si no cierra, algo se leyo mal.
        var history = await tariffs.GetHistoryAsync(ct);
        var rebuilt = BillCalculator.Build(parsed.ReadingFrom, parsed.ReadingTo, parsed.Kwh, history);
        var difference = Math.Round(rebuilt.Total - parsed.Total, 2);

        if (Math.Abs(difference) > 5m)
        {
            warnings.Add(
                $"El total recalculado ({rebuilt.Total:N2}) difiere del impreso ({parsed.Total:N2}) en {difference:N2}. " +
                "Revisa los tramos y los impuestos cargados.");
        }

        return new BillImportResultDto(
            TariffService.MapBill(bill),
            schedules,
            rebuilt.Total,
            difference,
            warnings);
    }

    /// <summary>Deduce los cuadros tarifarios de la factura y los guarda.</summary>
    private async Task<List<TariffScheduleDto>> SaveSchedulesAsync(
        ParsedBill parsed, List<string> warnings, CancellationToken ct)
    {
        var result = TariffScheduleFactory.Build(parsed, DateTimeOffset.UtcNow);
        warnings.AddRange(result.Warnings);

        var saved = new List<TariffScheduleDto>();
        foreach (var schedule in result.Schedules)
        {
            var stored = await tariffs.UpsertAsync(schedule, ct);
            saved.Add(TariffService.Map(stored));
        }

        return saved;
    }

    /// <summary>
    /// Borra los cuadros importados que repiten exactamente el precio del anterior.
    ///
    /// Cada factura re-ancla en su fecha de lectura precios que ya venian rigiendo desde fin del
    /// mes anterior, asi que sin esto el historial muestra el doble de filas de las que hubo
    /// cambios reales. Solo toca cuadros que vinieron de una factura: los cargados a mano
    /// quedan como los dejo el usuario.
    /// </summary>
    private async Task CompactAsync(CancellationToken ct)
    {
        var history = (await tariffs.GetHistoryAsync(ct)).OrderBy(s => s.ValidFrom).ToList();

        for (var i = 1; i < history.Count; i++)
        {
            var current = history[i];
            if (!current.Source.StartsWith("Factura", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!SamePricing(history[i - 1], current))
                continue;

            await tariffs.DeleteAsync(current.Id, ct);
            logger.LogDebug(
                "Cuadro del {Date} borrado por repetir los precios del {Previous}.",
                current.ValidFrom, history[i - 1].ValidFrom);
        }
    }

    /// <summary>
    /// Dos cuadros son el mismo precio si coinciden cargo fijo, tramos, recargos y cargos del
    /// periodo.
    ///
    /// Todo se compara con tolerancia, no por igualdad exacta: el cargo fijo sale de dividir un
    /// importe por los dias, asi que dos facturas dan el mismo valor con distinto arrastre
    /// (122,0348 y 122,035), y las tasas salen de dividir contra el importe basico.
    /// </summary>
    private static bool SamePricing(TariffSchedule a, TariffSchedule b)
    {
        if (Math.Abs(a.FixedChargePerDay - b.FixedChargePerDay) > 0.01m)
            return false;

        var blocksA = a.Blocks.OrderBy(x => x.Order).ToList();
        var blocksB = b.Blocks.OrderBy(x => x.Order).ToList();

        if (blocksA.Count != blocksB.Count)
            return false;

        for (var i = 0; i < blocksA.Count; i++)
        {
            if (blocksA[i].UpToKwh != blocksB[i].UpToKwh ||
                Math.Abs(blocksA[i].PricePerKwh - blocksB[i].PricePerKwh) > 0.001m)
            {
                return false;
            }
        }

        return SameCharges(
                   a.Surcharges.Select(x => (x.Name, x.Rate)),
                   b.Surcharges.Select(x => (x.Name, x.Rate)),
                   tolerance: 0.0001m)
               && SameCharges(
                   a.PeriodCharges.Select(x => (x.Name, x.Amount)),
                   b.PeriodCharges.Select(x => (x.Name, x.Amount)),
                   tolerance: 0.01m);
    }

    private static bool SameCharges(
        IEnumerable<(string Name, decimal Value)> a,
        IEnumerable<(string Name, decimal Value)> b,
        decimal tolerance)
    {
        var listA = a.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        var listB = b.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

        return listA.Count == listB.Count
               && listA.Zip(listB).All(pair =>
                   pair.First.Name == pair.Second.Name &&
                   Math.Abs(pair.First.Value - pair.Second.Value) <= tolerance);
    }

}
