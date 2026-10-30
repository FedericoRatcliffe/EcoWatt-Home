namespace EcoWattCasa.Application.Common;

/// <summary>
/// Proyeccion del ciclo y comparacion con el anterior, en funcion de las horas que se midieron
/// de verdad y no de los dias del calendario.
///
/// Contar dias rompe en los bordes: a la 1 de la manana del primer dia del ciclo, una hora de
/// consumo se toma como un dia entero y la proyeccion da la vigesima parte de lo real. Y un
/// ciclo con la API apagada, o en el que se borro el historial, no tiene cuatro horas de
/// consumo: tiene cuatro horas medidas, y compararlo contra uno completo da cualquier cosa.
/// </summary>
public static class CycleProjection
{
    /// <summary>
    /// Un dia entero medido. Con menos, el ritmo depende de la hora: la noche sola proyecta de
    /// menos y la tarde sola de mas.
    /// </summary>
    public const int MinimumMeasuredHours = 24;

    /// <summary>
    /// Cobertura minima del ciclo anterior para compararse. Deja pasar un corte de luz o un
    /// reinicio de la PC (tres dias de 30), no un ciclo medido a medias.
    /// </summary>
    public const double ComparableCoverage = 0.9;

    /// <summary>
    /// kWh al cierre si se sostiene el ritmo de las horas medidas. null si todavia no hay un
    /// dia entero de datos.
    /// </summary>
    public static double? ProjectKwh(double measuredKwh, int measuredHours, int totalDays)
    {
        if (measuredHours < MinimumMeasuredHours || totalDays <= 0)
            return null;

        return measuredKwh / measuredHours * totalDays * 24d;
    }

    /// <summary>Si el periodo se midio lo suficiente como para comparar su factura con otra.</summary>
    public static bool IsComparable(int measuredHours, PeriodWindow window)
        => window.Days > 0 && measuredHours >= window.Days * 24d * ComparableCoverage;
}
