using System.Globalization;
using System.Text.RegularExpressions;

namespace EcoWattCasa.Application.Billing;

/// <summary>Un renglon de "DETALLE TARIFAS APLICADAS": concepto, dias, cantidad y precio.</summary>
/// <param name="Days">Dias del periodo de precio al que corresponde el renglon.</param>
/// <param name="BlockOrder">1, 2, 3... para los tramos variables; 0 para el cargo fijo.</param>
public sealed record BillDetailLine(
    string Concept,
    int Days,
    double Quantity,
    decimal UnitPrice,
    decimal Amount,
    int BlockOrder,
    double? UpToKwh);

/// <summary>Un renglon de impuestos o gravamenes.</summary>
public sealed record BillTaxLine(string Name, decimal Amount, bool IsProportional);

/// <summary>Factura leida del PDF, sin interpretar todavia como cuadros tarifarios.</summary>
public sealed record ParsedBill(
    string InvoiceNumber,
    string Period,
    DateOnly ReadingFrom,
    DateOnly ReadingTo,
    int Days,
    double Kwh,
    double MeterStart,
    double MeterEnd,
    decimal BasicAmount,
    decimal TotalTaxes,
    decimal Total,
    IReadOnlyList<BillDetailLine> Details,
    IReadOnlyList<BillTaxLine> Taxes,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Lee el texto de una factura de la Cooperativa de Venado Tuerto y devuelve sus numeros.
///
/// El parser trabaja sobre el texto plano del PDF, que viene con las dos columnas del papel
/// entremezcladas, asi que busca cada dato por su etiqueta en lugar de por posicion.
/// </summary>
public static class BillParser
{
    /// <summary>Conceptos de II-IMPUESTOS que son un porcentaje del importe basico.</summary>
    private static readonly string[] ProportionalTaxKeywords =
        ["iva", "ley pcial. 10014", "ley pcial 10014", "cap.rem", "cap rem", "cap.inv", "cap inv"];

    /// <summary>Conceptos que son un monto fijo por periodo facturado.</summary>
    private static readonly string[] FixedTaxKeywords =
        ["alum", "12692"];

    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    public static ParsedBill Parse(string text)
    {
        var warnings = new List<string>();
        var lines = Normalize(text);
        var joined = string.Join("\n", lines);

        var period = Match(joined, @"Per[ií]odo:?\s*(\d{2}/\d{4})")
            ?? throw new BillParseException("No se encontro el periodo de la factura.");

        var invoice = Match(joined, @"Control\s*:?\s*(\d{4}-\d+)")
            ?? Match(joined, @"\b(\d{4}-\d{8})\b")
            ?? "sin-numero";

        var reading = ParseReading(lines)
            ?? throw new BillParseException("No se encontro el renglon de lectura del medidor.");

        var basic = ParseAmount(joined, @"Importe B[áa]sico Energ[íi]a El[ée]ctrica\s+([\d.,]+)")
            ?? throw new BillParseException("No se encontro el importe basico.");

        var total = ParseAmount(joined, @"TOTAL A PAGAR hasta el:?\s*\d{2}/\d{2}/\d{4}\s*\$?\s*([\d.,]+)")
            ?? ParseAmount(joined, @"Total\s+Energia\s+([\d.,]+)")
            ?? throw new BillParseException("No se encontro el total a pagar.");

        var details = ParseDetails(lines, warnings);
        var taxes = ParseTaxes(lines, warnings);

        if (details.Count == 0)
            warnings.Add("No se pudo leer 'DETALLE TARIFAS APLICADAS': sin eso no se deducen los precios.");

        var totalTaxes = ParseAmount(joined, @"Subtotal Impuestos/Grav[áa]menes[^\d]*([\d.,]+)")
                         ?? taxes.Sum(t => t.Amount);

        return new ParsedBill(
            invoice,
            period,
            reading.From,
            reading.To,
            reading.Days,
            reading.Kwh,
            reading.MeterStart,
            reading.MeterEnd,
            basic,
            totalTaxes,
            total,
            details,
            taxes,
            warnings);
    }

    /// <summary>
    /// Renglon del medidor. El extractor devuelve la fila invertida y con los numeros pegados
    /// a las fechas, tal cual sale del PDF de dos columnas:
    ///   "C 175 1 28.058,0031/07/2026 27.883,0002/07/2026"
    /// es decir: TL, consumo, rel. transf., estado y fecha actual, estado y fecha anterior.
    /// Se prueba tambien el orden natural por si otro extractor lo entrega derecho.
    /// </summary>
    private static (DateOnly From, DateOnly To, int Days, double Kwh, double MeterStart, double MeterEnd)?
        ParseReading(IReadOnlyList<string> lines)
    {
        var inverted = new Regex(
            @"^[A-Z]+\s+(?<kwh>[\d.]+)\s+\d+\s+(?<endState>[\d.]*\d,\d{2})(?<endDate>\d{2}/\d{2}/\d{4})\s+(?<startState>[\d.]*\d,\d{2})(?<startDate>\d{2}/\d{2}/\d{4})",
            RegexOptions.CultureInvariant);

        var natural = new Regex(
            @"(?<startDate>\d{2}/\d{2}/\d{4})\s+(?<startState>[\d.,]+)\s+(?<endDate>\d{2}/\d{2}/\d{4})\s+(?<endState>[\d.,]+)\s+\d+\s+(?<kwh>\d+)\s+[A-Z]",
            RegexOptions.CultureInvariant);

        foreach (var line in lines)
        {
            var match = inverted.Match(line);
            if (!match.Success)
                match = natural.Match(line);
            if (!match.Success)
                continue;

            var from = ParseDate(match.Groups["startDate"].Value);
            var to = ParseDate(match.Groups["endDate"].Value);
            if (from is null || to is null || to <= from)
                continue;

            var meterStart = ParseNumber(match.Groups["startState"].Value) ?? 0d;
            var meterEnd = ParseNumber(match.Groups["endState"].Value) ?? 0d;
            var kwh = ParseNumber(match.Groups["kwh"].Value) ?? meterEnd - meterStart;

            return (from.Value, to.Value, to.Value.DayNumber - from.Value.DayNumber, kwh, meterStart, meterEnd);
        }

        return null;
    }

    /// <summary>
    /// Renglones tipo "Cargo Variable hasta 75 kWh 26 Dias 65 X $214,19 $13.922,35"
    /// y "Cargo Fijo 26 Dias 1 X $2.705,22 $2.705,22".
    /// </summary>
    private static List<BillDetailLine> ParseDetails(IReadOnlyList<string> lines, List<string> warnings)
    {
        // "$13.922,3526 Dias                65 X      $214,19Cargo Variable hasta 75 kWh"
        // El importe queda pegado a los dias y el precio al concepto; el ancla es que los
        // importes siempre terminan en coma y dos decimales.
        var pattern = new Regex(
            @"^\$(?<amount>[\d.]*\d,\d{2})(?<days>\d{1,3})\s+Dias\s+(?<qty>[\d.,]+)\s+X\s+\$(?<price>[\d.]*\d,\d{2})(?<concept>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var topPattern = new Regex(@"(?<top>\d+)\s*kWh", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var result = new List<BillDetailLine>();
        var blockOrders = new Dictionary<double, int>();

        foreach (var line in lines)
        {
            var match = pattern.Match(line);
            if (!match.Success)
                continue;

            var days = int.Parse(match.Groups["days"].Value);
            var quantity = ParseNumber(match.Groups["qty"].Value) ?? 0d;
            var price = ParseDecimal(match.Groups["price"].Value) ?? 0m;
            var amount = ParseDecimal(match.Groups["amount"].Value) ?? (decimal)quantity * price;

            var concept = match.Groups["concept"].Value.Trim();
            var topMatch = topPattern.Match(concept);

            double? top = null;
            var order = 0;

            if (topMatch.Success)
            {
                top = double.Parse(topMatch.Groups["top"].Value, CultureInfo.InvariantCulture);
                if (!blockOrders.TryGetValue(top.Value, out order))
                {
                    order = blockOrders.Count + 1;
                    blockOrders[top.Value] = order;
                }
            }
            else if (!concept.Contains("Fijo", StringComparison.OrdinalIgnoreCase))
            {
                // Ni tramo ni cargo fijo: no es un renglon de tarifa.
                continue;
            }

            result.Add(new BillDetailLine(concept, days, quantity, price, amount, order, top));
        }

        // Los topes tienen que quedar ordenados de menor a mayor para que los tramos cierren.
        var sortedTops = blockOrders.Keys.OrderBy(t => t).ToList();
        if (!sortedTops.SequenceEqual(blockOrders.OrderBy(kv => kv.Value).Select(kv => kv.Key)))
        {
            warnings.Add("Los tramos no vinieron ordenados en el PDF; se reordenaron por tope de kWh.");
            var fixedOrders = sortedTops.Select((top, i) => (top, order: i + 1)).ToDictionary(x => x.top, x => x.order);
            result = result
                .Select(l => l.UpToKwh is { } t ? l with { BlockOrder = fixedOrders[t] } : l)
                .ToList();
        }

        return result;
    }

    /// <summary>Renglones de la seccion II-IMPUESTOS/GRAVAMENES.</summary>
    private static List<BillTaxLine> ParseTaxes(IReadOnlyList<string> lines, List<string> warnings)
    {
        var result = new List<BillTaxLine>();
        var inSection = false;

        // "Ley Pcial. 10014 3.033,18Ley Pcial. 10014": el extractor repite el concepto detras
        // del importe. Se acepta con o sin esa repeticion.
        var pattern = new Regex(
            @"^(?<name>.+?)\s+(?<amount>[\d.]*\d,\d{2})(?<tail>.*)$",
            RegexOptions.CultureInvariant);

        foreach (var line in lines)
        {
            if (line.Contains("II-IMPUESTOS", StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (!inSection)
                continue;

            if (line.StartsWith("Subtotal", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Total", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("III-", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var match = pattern.Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value.Trim();
            var amount = ParseDecimal(match.Groups["amount"].Value);
            var tail = match.Groups["tail"].Value.Trim();

            if (amount is null || name.Length == 0)
                continue;

            // Si hay cola, tiene que ser el mismo concepto repetido; si no, el renglon es otra cosa.
            if (tail.Length > 0 && !tail.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            var lower = name.ToLowerInvariant();
            if (ProportionalTaxKeywords.Any(lower.Contains))
            {
                result.Add(new BillTaxLine(name, amount.Value, IsProportional: true));
            }
            else if (FixedTaxKeywords.Any(lower.Contains))
            {
                result.Add(new BillTaxLine(name, amount.Value, IsProportional: false));
            }
            else
            {
                // Ante la duda se carga como monto fijo: para esta factura da el mismo total,
                // y el aviso queda para revisarlo a mano.
                result.Add(new BillTaxLine(name, amount.Value, IsProportional: false));
                warnings.Add($"Concepto desconocido '{name}': se cargo como cargo fijo del periodo. Revisalo si en realidad es un porcentaje.");
            }
        }

        return result;
    }

    private static List<string> Normalize(string text)
        => text
            .Replace(' ', ' ')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => Regex.Replace(l.Replace('\r', ' '), @"\s+", " ").Trim())
            .Where(l => l.Length > 0)
            .ToList();

    private static string? Match(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static decimal? ParseAmount(string text, string pattern)
        => ParseDecimal(Match(text, pattern));

    /// <summary>Numeros en formato argentino: 43.541,83.</summary>
    public static decimal? ParseDecimal(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : decimal.TryParse(value.Trim(), NumberStyles.Number, Ar, out var parsed) ? parsed : null;

    public static double? ParseNumber(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : double.TryParse(value.Trim(), NumberStyles.Number, Ar, out var parsed) ? parsed : null;

    private static DateOnly? ParseDate(string value)
        => DateOnly.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}

public sealed class BillParseException(string message) : Exception(message);
