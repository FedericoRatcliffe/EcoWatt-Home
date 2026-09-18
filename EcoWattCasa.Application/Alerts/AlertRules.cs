using EcoWattCasa.Application.Common;
using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Application.Alerts;

public enum AlertSeverity
{
    /// <summary>Algo que conviene saber, sin nada que hacer.</summary>
    Info,

    /// <summary>Algo que va a costar plata o que esta midiendo mal.</summary>
    Warning,

    /// <summary>El sistema no esta viendo lo que pasa en la casa.</summary>
    Critical
}

/// <param name="Code">Identificador estable de la regla, para el frontend.</param>
/// <param name="DeviceId">Cuando la alerta es de un aparato puntual.</param>
public sealed record Alert(
    string Code,
    AlertSeverity Severity,
    string Title,
    string Detail,
    Guid? DeviceId = null);

/// <summary>Estado de un dispositivo al momento de evaluar.</summary>
/// <param name="IsHouseMeter">
/// Mide toda la casa desde el tablero. Cambia el peso de que se calle: sin el medidor no hay
/// total de la casa, y sin total no hay factura estimada ni alerta de cruce de tramo.
/// </param>
public sealed record DeviceStatus(
    Guid Id,
    string Name,
    bool IsActive,
    DateTimeOffset? LastSeenUtc,
    bool IsHouseMeter = false);

public sealed class AlertThresholds
{
    public const string SectionName = "Alerts";

    public bool Enabled { get; set; } = true;

    /// <summary>Minutos sin reportar despues de los cuales se considera mudo un dispositivo.</summary>
    public int SilenceMinutes { get; set; } = 10;

    /// <summary>Cuanto tiene que superar la proyeccion al ciclo anterior para avisar (fraccion).</summary>
    public decimal OverrunRatio { get; set; } = 0.15m;
}

/// <summary>Todo lo que las reglas necesitan para decidir, ya resuelto.</summary>
public sealed record AlertContext(
    PeriodWindow Cycle,
    int ElapsedDays,
    double CycleKwh,
    double ProjectedKwh,
    decimal ProjectedCost,
    decimal PreviousCycleCost,
    TariffSchedule? Tariff,
    IReadOnlyList<DeviceStatus> Devices);

/// <summary>
/// Las reglas de alerta. Son una funcion pura del estado: reciben el contexto ya armado y no
/// tocan base ni reloj del sistema, asi que cada caso se puede verificar en un test.
///
/// La mas importante es el cruce de tramo. Con la tarifa por tramos de la cooperativa, pasar
/// de los 150 kWh en un ciclo sube el kWh marginal de ~$376 a ~$503: es el unico momento del
/// mes en que saber algo con anticipacion cambia lo que uno hace.
/// </summary>
public static class AlertRules
{
    public static IReadOnlyList<Alert> Evaluate(AlertContext context, AlertThresholds thresholds, DateTimeOffset nowUtc)
    {
        var alerts = new List<Alert>();

        alerts.AddRange(Telemetry(context, thresholds, nowUtc));
        alerts.AddRange(BlockCrossing(context));
        alerts.AddRange(Overrun(context, thresholds));

        // Lo mas grave primero; dentro de la misma gravedad, el orden en que se generaron.
        return alerts.OrderByDescending(a => a.Severity).ToList();
    }

    // ---------- Telemetria ----------

    /// <summary>
    /// Un enchufe que dejo de reportar no se nota mirando el dashboard: los totales
    /// simplemente quedan bajos. Por eso el silencio es una alerta y no un detalle.
    /// </summary>
    private static IEnumerable<Alert> Telemetry(AlertContext context, AlertThresholds thresholds, DateTimeOffset nowUtc)
    {
        var active = context.Devices.Where(d => d.IsActive).ToList();
        if (active.Count == 0)
            yield break;

        var silenceAfter = TimeSpan.FromMinutes(Math.Max(1, thresholds.SilenceMinutes));
        var silent = active.Where(d => IsSilent(d, nowUtc, silenceAfter)).ToList();

        if (silent.Count == 0)
            yield break;

        // Si callaron todos, el problema no es de un aparato: es el broker o la red.
        if (silent.Count == active.Count)
        {
            yield return new Alert(
                "telemetry-down",
                AlertSeverity.Critical,
                "No llega telemetria",
                $"Ninguno de los {active.Count} dispositivos reporta hace mas de {Plural(silenceAfter.TotalMinutes, "minuto")}. " +
                "Revisa que el broker MQTT este levantado y que los enchufes tengan red.");

            yield break;
        }

        // Que se calle el medidor de tablero y que se calle un enchufe son dos problemas
        // distintos, y antes de instalar el EM2 no lo eran. El medidor es el total de la casa:
        // sin el no hay factura estimada ni alerta de cruce de tramo. Un enchufe mudo, en
        // cambio, no cambia el total: su consumo se corre al no identificado.
        var hasMeter = active.Any(d => d.IsHouseMeter);

        foreach (var device in silent)
        {
            var since = device.LastSeenUtc is { } seen
                ? $"hace {Humanize(nowUtc - seen)}"
                : "desde que se registro";

            if (device.IsHouseMeter)
            {
                yield return new Alert(
                    "meter-silent",
                    AlertSeverity.Critical,
                    $"{device.Name} no reporta",
                    $"No manda lecturas {since}. Es el medidor de toda la casa: mientras este mudo, " +
                    "el consumo total, la factura estimada y el aviso de cruce de tramo quedan sin base.",
                    device.Id);

                continue;
            }

            var impact = hasMeter
                ? "El total de la casa lo sigue midiendo el tablero, asi que lo que consuma este " +
                  "aparato aparece como consumo no identificado."
                : "Mientras tanto, el consumo de la casa queda subestimado.";

            yield return new Alert(
                "device-silent",
                AlertSeverity.Warning,
                $"{device.Name} no reporta",
                $"No manda lecturas {since}. {impact}",
                device.Id);
        }
    }

    private static bool IsSilent(DeviceStatus device, DateTimeOffset nowUtc, TimeSpan silenceAfter)
        => device.LastSeenUtc is null || nowUtc - device.LastSeenUtc.Value > silenceAfter;

    // ---------- Cruce de tramo ----------

    /// <summary>
    /// Avisa cuando la proyeccion del ciclo va a cruzar el tope de un tramo, y en que fecha.
    /// Si ya lo cruzo, informa a que precio quedo el kWh adicional.
    /// </summary>
    private static IEnumerable<Alert> BlockCrossing(AlertContext context)
    {
        if (context.Tariff is not { } tariff || context.CycleKwh <= 0)
            yield break;

        var surcharge = 1m + tariff.TotalSurchargeRate;

        // Topes por encima del consumo actual, de menor a mayor.
        var next = tariff.Blocks
            .OrderBy(b => b.Order)
            .Select(b => b.UpToKwh)
            .Where(top => top is not null && top > context.CycleKwh)
            .Select(top => top!.Value)
            .FirstOrDefault();

        if (next <= 0)
        {
            // Ya paso el ultimo tope con nombre: no hay nada mas que avisar.
            yield break;
        }

        var currentPrice = Math.Round(tariff.PriceAt(context.CycleKwh) * surcharge, 2);
        var priceAfter = Math.Round(tariff.PriceAt(next) * surcharge, 2);

        // Un tramo que no encarece no merece aviso.
        if (priceAfter <= currentPrice)
            yield break;

        if (context.ProjectedKwh < next)
        {
            yield break;
        }

        var crossingDay = EstimateCrossingDay(context, next);
        var when = crossingDay is { } day
            ? $"alrededor del {day:dd/MM}"
            : "antes de que cierre el ciclo";

        yield return new Alert(
            "block-crossing",
            AlertSeverity.Warning,
            $"Vas a cruzar los {next:0} kWh",
            $"Al ritmo actual llegas {when} ({context.ProjectedKwh:0.#} kWh proyectados). " +
            $"A partir de ahi cada kWh pasa de {Money(currentPrice)} a {Money(priceAfter)}.");
    }

    /// <summary>Dia estimado del cruce, extrapolando el consumo diario del ciclo.</summary>
    private static DateOnly? EstimateCrossingDay(AlertContext context, double boundaryKwh)
    {
        if (context.ElapsedDays <= 0)
            return null;

        var perDay = context.CycleKwh / context.ElapsedDays;
        if (perDay <= 0)
            return null;

        var daysToBoundary = (int)Math.Ceiling(boundaryKwh / perDay);
        var crossing = context.Cycle.From.AddDays(daysToBoundary - 1);

        return crossing < context.Cycle.To ? crossing : null;
    }

    // ---------- Proyeccion contra el ciclo anterior ----------

    private static IEnumerable<Alert> Overrun(AlertContext context, AlertThresholds thresholds)
    {
        if (context.PreviousCycleCost <= 0 || context.ProjectedCost <= 0)
            yield break;

        var ratio = (context.ProjectedCost - context.PreviousCycleCost) / context.PreviousCycleCost;
        if (ratio < thresholds.OverrunRatio)
            yield break;

        yield return new Alert(
            "cycle-overrun",
            AlertSeverity.Warning,
            "Vas arriba del ciclo anterior",
            $"La proyeccion del ciclo es {Money(context.ProjectedCost)}, un {ratio * 100:0.#} % mas que " +
            $"los {Money(context.PreviousCycleCost)} del anterior.");
    }

    // ---------- Formato ----------

    private static string Money(decimal value) => $"${value:N0}".Replace(",", ".");

    private static string Humanize(TimeSpan span) => span.TotalHours switch
    {
        < 1 => Plural(span.TotalMinutes, "minuto"),
        < 48 => Plural(span.TotalHours, "hora"),
        _ => Plural(span.TotalDays, "dia")
    };

    /// <summary>"1 minuto" y no "1 minutos": el texto lo lee una persona.</summary>
    private static string Plural(double value, string unit)
    {
        var rounded = (int)Math.Round(value);
        return rounded == 1 ? $"1 {unit}" : $"{rounded} {unit}s";
    }
}
