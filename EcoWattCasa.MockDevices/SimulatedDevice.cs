using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

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

/// <summary>Canal que no tiene pinza conectada: siempre cero.</summary>
internal sealed class IdleChannelProfile : LoadProfile
{
    public override double NextWatts(TimeSpan step, Random rng) => 0d;
}

/// <summary>
/// Lo que consume la casa y no pasa por ningun enchufe medido: luces, TV, microondas,
/// cargadores. Sube de tarde/noche y baja de madrugada.
/// </summary>
internal sealed class HouseholdBaselineProfile(double nightWatts, double dayWatts, double eveningWatts)
    : LoadProfile
{
    private DateTimeOffset _now;

    /// <summary>La curva depende de la hora, asi que el reloj lo pone quien simula.</summary>
    public void SetClock(DateTimeOffset now) => _now = now;

    public override double NextWatts(TimeSpan step, Random rng)
    {
        var watts = _now.Hour switch
        {
            >= 0 and < 7 => nightWatts,
            >= 19 and < 24 => eveningWatts,
            _ => dayWatts
        };

        return Math.Max(0, watts * (1 + (rng.NextDouble() - 0.5) * 0.25));
    }
}

/// <summary>
/// El canal del medidor de tablero: mide TODA la casa, o sea la suma de los enchufes mas
/// lo que no pasa por ninguno.
///
/// Que sea una suma y no un perfil independiente es lo que hace util al mock: el
/// "consumo no identificado" que calcula el dashboard (total del EM2 menos la suma de los
/// enchufes) da exactamente el perfil de base, y nunca negativo. Con dos perfiles sueltos
/// la resta daria cualquier cosa y no se podria ver si la cuenta esta bien.
///
/// Depende de que los enchufes se avancen ANTES que el medidor en cada paso.
/// </summary>
internal sealed class HouseTotalProfile(IReadOnlyList<SimulatedDevice> plugs, HouseholdBaselineProfile baseline)
    : LoadProfile
{
    public override double NextWatts(TimeSpan step, Random rng)
        => plugs.Sum(p => p.LastWatts) + baseline.NextWatts(step, rng);
}

/// <summary>
/// Un equipo Tasmota simulado. Sirve para los dos modelos comprados:
///
///   - Enchufe Athom PG05V3: un canal de energia y rele.
///   - Athom EM2: dos canales de corriente, uno de tension y sin rele. Con
///     <c>SO129 1</c> publica la energia por canal, o sea arrays en el JSON.
///
/// Mantiene su propio contador de kWh acumulados por canal, como el equipo real.
/// </summary>
internal sealed class SimulatedDevice
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly LoadProfile[] _profiles;
    private readonly DateTimeOffset _totalStart;
    private readonly double[] _totalKwh;
    private readonly double[] _todayKwh;
    private readonly double[] _yesterdayKwh;
    private DateOnly _currentDay;

    public SimulatedDevice(
        string topic,
        DateTimeOffset totalStart,
        LoadProfile[] profiles,
        bool hasRelay = true,
        bool splitTotals = false)
    {
        Topic = topic;
        _totalStart = totalStart;
        _profiles = profiles;
        HasRelay = hasRelay;
        SplitTotals = splitTotals;

        _totalKwh = new double[profiles.Length];
        _todayKwh = new double[profiles.Length];
        _yesterdayKwh = new double[profiles.Length];
    }

    /// <summary>Un enchufe: un canal y rele.</summary>
    public static SimulatedDevice Plug(string topic, DateTimeOffset totalStart, LoadProfile profile)
        => new(topic, totalStart, [profile]);

    public string Topic { get; }

    /// <summary>El EM2 no tiene rele: no hay nada que conmutar.</summary>
    public bool HasRelay { get; }

    /// <summary>Equivale a <c>SetOption129 1</c>: publica la energia por canal en vez de sumada.</summary>
    public bool SplitTotals { get; }

    public int ChannelCount => _profiles.Length;

    /// <summary>Estado del rele. Con el rele abierto el consumo es cero, como en el aparato real.</summary>
    public bool RelayOn { get; set; } = true;

    /// <summary>Watts del ultimo paso, sumando canales. Lo lee el medidor para armar el total.</summary>
    public double LastWatts { get; private set; }

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
                Array.Copy(_todayKwh, _yesterdayKwh, _todayKwh.Length);
                Array.Clear(_todayKwh);
            }

            _currentDay = day;
        }

        // Una sola medicion de tension para todo el equipo: el EM2 tiene un solo canal de
        // tension aunque tenga dos de corriente.
        var voltage = 220 + (rng.NextDouble() - 0.5) * 8;
        var channels = new ChannelSample[_profiles.Length];
        var total = 0d;

        for (var i = 0; i < _profiles.Length; i++)
        {
            var watts = HasRelay && !RelayOn ? 0d : _profiles[i].NextWatts(step, rng);
            var kwh = watts / 1000d * step.TotalHours;

            _totalKwh[i] += kwh;
            _todayKwh[i] += kwh;
            total += watts;

            var factor = watts > 5 ? Math.Clamp(0.97 - rng.NextDouble() * 0.08, 0.5, 1) : 0d;
            var apparent = factor > 0 ? watts / factor : 0d;
            var reactive = Math.Sqrt(Math.Max(0, apparent * apparent - watts * watts));
            var current = voltage > 0 ? apparent / voltage : 0d;

            channels[i] = new ChannelSample(
                Math.Round(watts, 1),
                Math.Round(apparent, 1),
                Math.Round(reactive, 1),
                Math.Round(factor, 2),
                Math.Round(current, 3),
                Math.Round(_totalKwh[i], 3),
                Math.Round(_todayKwh[i], 3),
                Math.Round(_yesterdayKwh[i], 3));
        }

        LastWatts = total;
        return new Sample(now, Math.Round(voltage, 1), channels);
    }

    /// <summary>Serializa la muestra con el formato exacto de Tasmota.</summary>
    public string ToTasmotaJson(Sample sample)
    {
        var energy = new JsonObject
        {
            ["TotalStartTime"] = _totalStart.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
        };

        Add("Total", c => c.TotalKwh);
        Add("Yesterday", c => c.YesterdayKwh);
        Add("Today", c => c.TodayKwh);
        Add("Power", c => c.Watts);
        Add("ApparentPower", c => c.ApparentPower);
        Add("ReactivePower", c => c.ReactivePower);
        Add("Factor", c => c.Factor);

        // Tension siempre escalar, incluso con SO129: el equipo mide un solo punto.
        energy["Voltage"] = sample.Voltage;

        Add("Current", c => c.Current);

        var payload = new JsonObject
        {
            ["Time"] = sample.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["ENERGY"] = energy
        };

        return payload.ToJsonString(Json);

        void Add(string name, Func<ChannelSample, double> select)
        {
            if (SplitTotals)
            {
                var array = new JsonArray();
                foreach (var channel in sample.Channels)
                    array.Add(JsonValue.Create(select(channel)));

                energy[name] = array;
            }
            else
            {
                // Sin SO129 Tasmota publica la suma de los canales en un solo numero.
                energy[name] = Math.Round(sample.Channels.Sum(select), 3);
            }
        }
    }

    /// <summary>Lo que mide un canal: potencia, tension aparte porque es comun al equipo.</summary>
    internal readonly record struct ChannelSample(
        double Watts,
        double ApparentPower,
        double ReactivePower,
        double Factor,
        double Current,
        double TotalKwh,
        double TodayKwh,
        double YesterdayKwh);

    internal readonly record struct Sample(
        DateTimeOffset Timestamp,
        double Voltage,
        ChannelSample[] Channels)
    {
        /// <summary>Potencia de todo el equipo, sumando canales. Es lo que se muestra en consola.</summary>
        public double Watts => Channels.Sum(c => c.Watts);
    }
}
