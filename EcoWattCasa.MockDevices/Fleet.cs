namespace EcoWattCasa.MockDevices;

/// <summary>
/// Los cinco equipos Athom comprados, simulados: el medidor de tablero y los cuatro enchufes.
/// Los topics son los mismos que siembra la base, asi que el mock cae sobre los dispositivos
/// ya registrados sin tener que dar de alta nada.
///
/// Existe como tipo propio, y no como un array suelto, por una razon: el medidor mide la suma
/// de los enchufes, asi que tiene que avanzar DESPUES que ellos en cada paso. Esa dependencia
/// queda encerrada en <see cref="Step"/> en vez de depender del orden en que alguien itere.
/// </summary>
internal sealed class Fleet
{
    private readonly HouseholdBaselineProfile _baseline;

    private Fleet(SimulatedDevice meter, SimulatedDevice[] plugs, HouseholdBaselineProfile baseline)
    {
        Meter = meter;
        Plugs = plugs;
        _baseline = baseline;
        All = [.. plugs, meter];
    }

    /// <summary>El EM2 del tablero: dos canales, sin rele, publicando con SO129 1.</summary>
    public SimulatedDevice Meter { get; }

    public SimulatedDevice[] Plugs { get; }

    /// <summary>Todos los equipos, en orden de avance: los enchufes primero, el medidor al final.</summary>
    public SimulatedDevice[] All { get; }

    public static Fleet Build()
    {
        // El contador acumulado arranca con historia, como un equipo que ya venia instalado.
        var totalStart = DateTimeOffset.UtcNow.AddMonths(-3);

        // Los perfiles estan calibrados contra las facturas reales de la casa: ~175 kWh/mes,
        // o sea 243 W promedio. Los enchufes ponen ~132 W de esos y el resto (~108 W) es el
        // consumo que no pasa por ningun enchufe medido.
        SimulatedDevice[] plugs =
        [
            // Heladera: compresor 14 min prendido a 130 W, 26 min en reposo. ~1,1 kWh/dia.
            // En la base va con el rele bloqueado, pero el equipo fisico igual lo tiene:
            // el que no deja apagarla es el backend, no el enchufe.
            SimulatedDevice.Plug("plug-heladera", totalStart,
                new DutyCycleProfile(TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(26), wattsOn: 130, wattsOff: 2)),

            // PC con monitores: reposo bajo y picos cuando compila o juega. ~1,7 kWh/dia.
            SimulatedDevice.Plug("plug-pc", totalStart,
                new RandomWalkProfile(idleWatts: 65, minWatts: 35, maxWatts: 240)),

            // Lavarropas: un ciclo de 45 min por dia y el resto en standby. ~0,36 kWh/dia.
            SimulatedDevice.Plug("plug-lavarropas", totalStart,
                new DutyCycleProfile(TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(1395), wattsOn: 450, wattsOff: 1)),

            // El cuarto enchufe todavia no tiene nada conectado: mide cero, que es un caso
            // que conviene tener en el mock (tarjetas en cero, promedios con divisor cero).
            SimulatedDevice.Plug("plug-libre", totalStart, new IdleChannelProfile())
        ];

        // Luces, TV, microondas, cargadores: ~108 W promedio con pico a la noche.
        var baseline = new HouseholdBaselineProfile(nightWatts: 65, dayWatts: 95, eveningWatts: 165);

        // El EM2 comprado trae una sola pinza, asi que el canal 1 queda sin medir.
        // Con SO129 1 publica los dos canales igual, el segundo en cero.
        var meter = new SimulatedDevice(
            "em2-tablero",
            totalStart,
            [new HouseTotalProfile(plugs, baseline), new IdleChannelProfile()],
            hasRelay: false,
            splitTotals: true);

        return new Fleet(meter, plugs, baseline);
    }

    /// <summary>
    /// Avanza toda la flota un paso y devuelve las muestras en el mismo orden que
    /// <see cref="All"/>. Los enchufes van primero para que el medidor sume sus watts de
    /// este paso y no los del anterior.
    /// </summary>
    public (SimulatedDevice Device, SimulatedDevice.Sample Sample)[] Step(
        TimeSpan step, DateTimeOffset now, Random rng)
    {
        _baseline.SetClock(now);

        // Se resuelve entero y no perezoso a proposito: si el consumidor cortara la
        // enumeracion a la mitad, el medidor se quedaria sin avanzar.
        var samples = new (SimulatedDevice, SimulatedDevice.Sample)[All.Length];
        for (var i = 0; i < All.Length; i++)
            samples[i] = (All[i], All[i].Step(step, now, rng));

        return samples;
    }
}
