namespace EcoWattCasa.Application.Common;

public enum BillCheckStatus
{
    /// <summary>No hay ninguna lectura de la casa en el periodo de la factura.</summary>
    NoData,

    /// <summary>Se midio solo una parte del periodo: la diferencia no dice nada del medidor.</summary>
    Partial,

    /// <summary>Lo medido coincide con lo facturado dentro de la tolerancia.</summary>
    Match,

    /// <summary>Periodo medido entero y aun asi no coincide: algo mide mal.</summary>
    Mismatch
}

/// <summary>Una factura real contra lo que midio el tablero en las mismas fechas.</summary>
/// <param name="CoveragePercent">Que parte de las horas del periodo tiene lectura de la casa.</param>
/// <param name="DifferencePercent">Medido contra facturado. null si no hay con que comparar.</param>
public sealed record BillCheckResult(
    double BilledKwh,
    double MeasuredKwh,
    double CoveragePercent,
    double? DifferencePercent,
    BillCheckStatus Status);

/// <summary>
/// Compara una factura con lo medido. Es lo que dice si se puede confiar en el resto del
/// sistema: si el EM2 coincide con el medidor de la cooperativa, las estimaciones valen.
///
/// Solo se compara un periodo medido casi entero. Con la API caida tres dias el medidor da
/// 10 % menos y no es culpa suya; mostrar ese -10 % como desvio haria desconfiar de lo que
/// esta bien.
/// </summary>
public static class BillCheck
{
    /// <summary>Que parte del periodo tiene que estar medida para comparar.</summary>
    public const double MinCoverage = 0.95;

    /// <summary>
    /// Desvio aceptable. Las fechas de lectura de la factura son dias y no horas: el lector
    /// pasa a cualquier hora, y eso solo ya mueve unos kWh de un periodo al otro.
    /// </summary>
    public const double Tolerance = 0.05;

    public static BillCheckResult Compare(PeriodWindow period, double billedKwh, double measuredKwh, int measuredHours)
    {
        var totalHours = period.Days * 24d;
        var coverage = totalHours > 0 ? Math.Min(1d, measuredHours / totalHours) : 0d;

        if (measuredHours == 0)
            return new BillCheckResult(billedKwh, 0d, 0d, null, BillCheckStatus.NoData);

        var difference = billedKwh > 0 ? (measuredKwh - billedKwh) / billedKwh : (double?)null;

        var status = coverage < MinCoverage || difference is null
            ? BillCheckStatus.Partial
            : Math.Abs(difference.Value) <= Tolerance ? BillCheckStatus.Match : BillCheckStatus.Mismatch;

        return new BillCheckResult(
            billedKwh,
            Math.Round(measuredKwh, 2),
            Math.Round(coverage * 100d, 1),
            difference is { } d ? Math.Round(d * 100d, 1) : null,
            status);
    }
}
