using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Application.Common;

/// <summary>Un cambio de consumo a simular: tantos watts, tantas horas por dia.</summary>
/// <param name="Watts">Positivo = se deja de consumir; negativo = se suma un aparato.</param>
/// <param name="HoursPerDay">Entre 0 y 24.</param>
public sealed record ConsumptionChange(double Watts, double HoursPerDay);

/// <summary>La factura de un ciclo con y sin el cambio.</summary>
/// <param name="DeltaKwh">kWh que se dejan de consumir en el ciclo (negativo si se suman).</param>
/// <param name="Saving">Lo que baja la factura. Negativo si sube.</param>
public sealed record SavingsResult(
    double BaselineKwh,
    double ScenarioKwh,
    double DeltaKwh,
    BillBreakdown Baseline,
    BillBreakdown Scenario,
    decimal Saving);

/// <summary>
/// "Cuanto ahorro si apago esto". Con tarifa por tramos la respuesta no es watts por precio
/// medio: sacar kWh del ciclo los saca del tramo mas caro en el que este parada la casa, y si
/// el cambio la baja de tramo, el precio de todos los kWh del tramo que se deja tambien cambia.
/// Por eso se arma la factura entera dos veces y se restan.
///
/// El cambio se aplica al ciclo completo y no a lo que queda: la pregunta es cuanto cambia una
/// factura, no cuanto se ahorra de aca al cierre.
/// </summary>
public static class SavingsSimulator
{
    public static SavingsResult Simulate(
        PeriodWindow cycle,
        double baselineKwh,
        ConsumptionChange change,
        IReadOnlyList<TariffSchedule> history)
    {
        var hours = Math.Clamp(change.HoursPerDay, 0d, 24d);
        var deltaKwh = change.Watts * hours * cycle.Days / 1000d;

        // No se puede consumir menos que cero: apagar 500 W en una casa de 100 kWh no da
        // una factura negativa.
        var scenarioKwh = Math.Max(0d, baselineKwh - deltaKwh);
        deltaKwh = baselineKwh - scenarioKwh;

        var baseline = BillCalculator.Build(cycle.From, cycle.To, baselineKwh, history);
        var scenario = BillCalculator.Build(cycle.From, cycle.To, scenarioKwh, history);

        return new SavingsResult(
            baselineKwh,
            scenarioKwh,
            deltaKwh,
            baseline,
            scenario,
            baseline.Total - scenario.Total);
    }

    /// <summary>
    /// Horas por dia de una franja local. La franja puede cruzar la medianoche (22 a 6 son 8
    /// horas), y desde igual a hasta es el dia entero.
    /// </summary>
    public static double HoursBetween(int fromHour, int toHour)
    {
        var span = ((toHour - fromHour) % 24 + 24) % 24;
        return span == 0 ? 24 : span;
    }
}
