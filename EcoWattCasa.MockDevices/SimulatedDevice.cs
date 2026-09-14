using System.Globalization;
using System.Text.Json;

namespace EcoWattCasa.MockDevices;

/// <summary>Perfil de carga: decide cuantos watts consume el aparato en cada paso.</summary>
internal abstract class LoadProfile
{
    public abstract double NextWatts(TimeSpan step, Random rng);
}

/// <summary>
/// Carga que prende y apaga en ciclos, como el compresor de una heladera o la resistencia
/// de un termotanque.
/// </summary>
internal sealed class DutyCycleProfile(TimeSpan onFor, TimeSpan offFor, double wattsOn, double wattsOff)
    : LoadProfile
{
    private TimeSpan _inPhase = TimeSpan.Zero;
    private bool _on;

    public override double NextWatts(TimeSpan step, Random rng)
    {
        _inPhase += step;
        var phaseLength = _on ? onFor : offFor;

        if (_inPhase >= phaseLength)
        {
            _on = !_on;
            _inPhase = TimeSpan.Zero;
        }

        var baseWatts = _on ? wattsOn : wattsOff;

        // Arranque del compresor: los primeros segundos pegan un pico.
        if (_on && _inPhase < TimeSpan.FromSeconds(20))
            baseWatts *= 1.6;

        return Math.Max(0, baseWatts * (1 + (rng.NextDouble() - 0.5) * 0.06));
    }
}

/// <summary>
/// Carga que se mueve sola alrededor de un valor de reposo, con picos ocasionales:
/// una PC con monitores.
/// </summary>
internal sealed class RandomWalkProfile : LoadProfile
{
    private readonly double _idleWatts;
    private readonly double _minWatts;
    private readonly double _maxWatts;
    private double _watts;

    public RandomWalkProfile(double idleWatts, double minWatts, double maxWatts)
    {
        _idleWatts = idleWatts;
        _minWatts = minWatts;
        _maxWatts = maxWatts;
        _watts = idleWatts;
    }

    public override double NextWatts(TimeSpan step, Random rng)
    {
        // Tira hacia el reposo, mas un empujon al azar.
        var pullToIdle = (_idleWatts - _watts) * 0.15;
        var noise = (rng.NextDouble() - 0.5) * 40;

        // Cada tanto arranca algo pesado (compilar, un juego) y se va al techo.
        if (rng.NextDouble() < 0.04)
            noise += rng.NextDouble() * (_maxWatts - _watts) * 0.8;

        _watts = Math.Clamp(_watts + pullToIdle + noise, _minWatts, _maxWatts);
        return _watts;
    }
}

/// <summary>
/// Un Sonoff POW R2 simulado: mantiene su contador de energia acumulada y arma el mismo
/// JSON que publica Tasmota en tele/{topic}/SENSOR.
/// </summary>
internal sealed class SimulatedDevice(string topic, LoadProfile profile, DateTimeOffset totalStart)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private double _totalKwh;
    private double _todayKwh;
    private double _yesterdayKwh;
    private DateOnly _currentDay;

    public string Topic { get; } = topic;

    /// <summary>Estado del rele. Con el rele abierto el consumo es cero, como en el aparato real.</summary>
    public bool RelayOn { get; set; } = true;

    public string TelemetryTopic => $"tele/{Topic}/SENSOR";
    public string ResultTopic => $"stat/{Topic}/RESULT";
    public string PowerStateTopic => $"stat/{Topic}/POWER";

    /// <summary>Avanza la simulacion un paso y devuelve la muestra resultante.</summary>
    public Sample Step(TimeSpan step, DateTimeOffset now, Random rng)
    {
        var day = DateOnly.FromDateTime(now.DateTime);
        if (_currentDay != day)
        {
            // Tasmota reinicia Today a medianoche y pasa el valor a Yesterday.
            if (_currentDay != default)
            {
                _yesterdayKwh = _todayKwh;
                _todayKwh = 0;
            }

            _currentDay = day;
        }

        var watts = RelayOn ? profile.NextWatts(step, rng) : 0d;
        var kwh = watts / 1000d * step.TotalHours;

        _totalKwh += kwh;
        _todayKwh += kwh;

        var voltage = 220 + (rng.NextDouble() - 0.5) * 8;
        var factor = watts > 5 ? Math.Clamp(0.97 - rng.NextDouble() * 0.08, 0.5, 1) : 0d;
        var apparent = factor > 0 ? watts / factor : 0d;
        var reactive = Math.Sqrt(Math.Max(0, apparent * apparent - watts * watts));
        var current = voltage > 0 ? apparent / voltage : 0d;

        return new Sample(
            now,
            Math.Round(watts, 1),
            Math.Round(apparent, 1),
            Math.Round(reactive, 1),
            Math.Round(factor, 2),
            Math.Round(voltage, 1),
            Math.Round(current, 3),
            Math.Round(_totalKwh, 3),
            Math.Round(_todayKwh, 3),
            Math.Round(_yesterdayKwh, 3));
    }

    /// <summary>Serializa la muestra con el formato exacto de Tasmota.</summary>
    public string ToTasmotaJson(Sample sample)
    {
        var payload = new
        {
            Time = sample.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ENERGY = new
            {
                TotalStartTime = totalStart.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Total = sample.TotalKwh,
                Yesterday = sample.YesterdayKwh,
                Today = sample.TodayKwh,
                Power = sample.Watts,
                ApparentPower = sample.ApparentPower,
                ReactivePower = sample.ReactivePower,
                Factor = sample.Factor,
                Voltage = sample.Voltage,
                Current = sample.Current
            }
        };

        return JsonSerializer.Serialize(payload, Json);
    }

    internal readonly record struct Sample(
        DateTimeOffset Timestamp,
        double Watts,
        double ApparentPower,
        double ReactivePower,
        double Factor,
        double Voltage,
        double Current,
        double TotalKwh,
        double TodayKwh,
        double YesterdayKwh);
}
