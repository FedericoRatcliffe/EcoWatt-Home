using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Application.Common;

public enum GoalStatus
{
    /// <summary>Al ritmo actual el ciclo cierra por debajo del objetivo (o ya cerro asi).</summary>
    OnTrack,

    /// <summary>Todavia se puede cumplir, pero al ritmo actual el ciclo cierra por encima.</summary>
    AtRisk,

    /// <summary>Lo consumido ya supera los kWh que el objetivo permitia: no hay vuelta.</summary>
    Exceeded,

    /// <summary>Los cargos fijos solos ya superan el objetivo: ni sin consumir nada se cumple.</summary>
    Unreachable
}

/// <summary>Donde esta parado un periodo respecto del objetivo de factura.</summary>
/// <param name="MinimumCost">Lo que sale la factura del periodo sin consumir un solo kWh.</param>
/// <param name="KwhBudget">kWh que se pueden consumir en el periodo sin pasar el objetivo.</param>
/// <param name="DaysLeft">Dias que faltan para el cierre, contando hoy. 0 si ya cerro.</param>
/// <param name="AllowedKwhPerDay">
/// Lo que se puede consumir por dia de aca al cierre para cumplir. null con el periodo cerrado.
/// </param>
/// <param name="CurrentKwhPerDay">El ritmo que viene llevando la casa en el periodo.</param>
/// <param name="ProjectedCost">Cierre proyectado al ritmo actual; con el periodo cerrado, el real.</param>
public sealed record GoalProgress(
    decimal Amount,
    decimal MinimumCost,
    double KwhBudget,
    double ConsumedKwh,
    double RemainingKwh,
    int DaysLeft,
    double? AllowedKwhPerDay,
    double CurrentKwhPerDay,
    decimal ProjectedCost,
    bool IsOpen,
    GoalStatus Status);

/// <summary>
/// Traduce un objetivo en pesos a kWh, que es lo unico que se puede controlar.
///
/// No es una regla de tres: la factura tiene cargos fijos que se pagan igual, tramos que
/// encarecen el kWh a medida que se consume y recargos sobre todo eso. Por eso el presupuesto
/// de kWh no se despeja con una formula sino que se busca sobre <see cref="BillCalculator"/>:
/// el numero que sale es exactamente el que daria la factura reconstruida, con el prorrateo
/// entre cuadros tarifarios incluido.
/// </summary>
public static class GoalPlanner
{
    /// <summary>Techo de la busqueda. Ninguna casa consume esto en un ciclo.</summary>
    private const double MaxKwh = 100_000d;

    public static GoalProgress Evaluate(
        decimal amount,
        PeriodWindow window,
        int elapsedDays,
        double consumedKwh,
        decimal projectedCost,
        IReadOnlyList<TariffSchedule> history)
    {
        var minimum = BillCalculator.Build(window.From, window.To, 0d, history).Total;
        var budget = KwhBudget(amount, window.From, window.To, history);

        var isOpen = elapsedDays < window.Days;
        var remaining = Math.Max(0d, budget - consumedKwh);

        // Hoy cuenta como dia que queda: lo que ya se consumio hoy esta en consumedKwh, y lo
        // que falta del dia todavia se puede cuidar.
        var daysLeft = isOpen ? window.Days - elapsedDays + 1 : 0;
        double? allowedPerDay = isOpen ? remaining / daysLeft : null;
        var currentPerDay = elapsedDays > 0 ? consumedKwh / elapsedDays : 0d;

        var status = minimum > amount ? GoalStatus.Unreachable
            : consumedKwh > budget ? GoalStatus.Exceeded
            : projectedCost > amount ? GoalStatus.AtRisk
            : GoalStatus.OnTrack;

        return new GoalProgress(
            amount,
            minimum,
            Math.Round(budget, 1),
            Math.Round(consumedKwh, 2),
            Math.Round(remaining, 2),
            daysLeft,
            allowedPerDay is { } a ? Math.Round(a, 2) : null,
            Math.Round(currentPerDay, 2),
            projectedCost,
            isOpen,
            status);
    }

    /// <summary>
    /// Mayor cantidad de kWh del periodo cuya factura no supera <paramref name="amount"/>.
    /// 0 si el objetivo esta por debajo de los cargos fijos.
    ///
    /// El total no es estrictamente creciente en los kWh: cuando el periodo cruza dos cuadros
    /// tarifarios, la factura reparte cada tramo en kWh enteros y un kWh mas puede pasar de un
    /// cuadro caro a uno barato. Son centavos, pero por eso la busqueda solo avanza sobre
    /// puntos que cumplen: lo que devuelve siempre da una factura dentro del objetivo.
    /// </summary>
    public static double KwhBudget(decimal amount, DateOnly from, DateOnly to, IReadOnlyList<TariffSchedule> history)
    {
        bool Fits(double kwh) => BillCalculator.Build(from, to, kwh, history).Total <= amount;

        if (!Fits(0d))
            return 0d;

        var low = 0d;
        var high = 100d;

        while (Fits(high))
        {
            low = high;
            if (high >= MaxKwh)
                return MaxKwh;

            high *= 2;
        }

        // Una decima de kWh alcanza: es menos de $100 aun en el tramo mas caro.
        while (high - low > 0.01d)
        {
            var mid = (low + high) / 2d;
            if (Fits(mid))
                low = mid;
            else
                high = mid;
        }

        // La busqueda se queda una fraccion por debajo del borde: si el borde es justo 175 kWh,
        // termina en 174,99. Se prueba el redondeo antes que el truncado.
        var rounded = Math.Round(low, 1);
        if (Fits(rounded))
            return rounded;

        var truncated = Math.Floor(low * 10d) / 10d;
        return Fits(truncated) ? truncated : low;
    }
}
