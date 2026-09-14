using EcoWattCasa.Domain.ValueObjects;

namespace EcoWattCasa.Application.Common;

/// <summary>
/// Convierte muestras crudas en kWh. Preferimos el contador acumulado del propio medidor
/// (ENERGY.Total): es energia real y no se desvia si se pierden muestras. Solo cuando ese
/// contador no existe, o dio un salto imposible, integramos la potencia instantanea.
/// </summary>
public static class EnergyMath
{
    /// <summary>Periodo de telemetria asumido cuando el bucket tiene una sola muestra.</summary>
    public static readonly TimeSpan AssumedTelemetryPeriod = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Techo fisico de un solo circuito monofasico de la casa. Sirve para detectar que el
    /// contador se reseteo: si el delta supera lo que se puede consumir en la ventana, el
    /// salto no es consumo, es un medidor que volvio a cero (o un Sonoff reflasheado).
    /// </summary>
    public const double MaxPlausibleWatts = 30_000d;

    /// <summary>
    /// kWh del bucket. <paramref name="bucketDuration"/> es cuanto dura la ventana completa,
    /// no el rango entre la primera y la ultima muestra: un dispositivo que reporto dos veces
    /// al principio de la hora igual pudo consumir toda la hora.
    /// </summary>
    public static double ToKwh(EnergySamples samples, bool preferCumulative, TimeSpan bucketDuration)
    {
        if (preferCumulative && samples is { FirstTotalKwh: { } first, LastTotalKwh: { } last })
        {
            // Delta en orden temporal, no MAX-MIN: si el contador volvio a cero (Sonoff
            // reflasheado, EnergyReset, o el mock reiniciado) el delta sale negativo y el
            // bucket cae a la integracion en vez de facturar el salto como consumo.
            var delta = last - first;
            var window = bucketDuration > AssumedTelemetryPeriod ? bucketDuration : AssumedTelemetryPeriod;
            var ceiling = MaxPlausibleWatts / 1000d * window.TotalHours;

            if (delta >= 0 && delta <= ceiling)
                return delta;
        }

        return IntegrateWatts(samples);
    }

    /// <summary>kWh = promedio de W / 1000 * horas cubiertas por las muestras.</summary>
    public static double IntegrateWatts(EnergySamples samples)
    {
        if (samples.SampleCount == 0)
            return 0d;

        var span = samples is { FirstTimestamp: { } first, LastTimestamp: { } last }
            ? last - first
            : TimeSpan.Zero;

        // Una sola muestra (o todas en el mismo instante): vale lo que dura un periodo de telemetria.
        if (span <= TimeSpan.Zero)
            span = AssumedTelemetryPeriod;

        return samples.AvgWatts / 1000d * span.TotalHours;
    }
}
