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
        // o sea 243 W promedio. Las cuatro zapatillas ponen ~120 W de esos y el resto (~123 W)
        // es lo que no pasa por ninguna.
        //
        // Cada enchufe mide una ZAPATILLA entera (PC, monitores, home theater, luces), no un
        // aparato: por eso el perfil es un paseo alrededor de un reposo bajo con picos altos
        // cuando la PC se usa, y no un ciclo de encendido/apagado como el de un compresor.
        SimulatedDevice[] plugs =
        [
            SimulatedDevice.Plug("plug-paula", totalStart,
                new RandomWalkProfile(idleWatts: 14, minWatts: 7, maxWatts: 450, spikeChance: 0.010)),

            SimulatedDevice.Plug("plug-andy-1", totalStart,
                new RandomWalkProfile(idleWatts: 16, minWatts: 8, maxWatts: 600, spikeChance: 0.010)),

            SimulatedDevice.Plug("plug-andy-2", totalStart,
                new RandomWalkProfile(idleWatts: 15, minWatts: 8, maxWatts: 600, spikeChance: 0.008)),

            // La mas cargada: Ryzen 5 5500 + RX 7600 dan ~400 W de la pared jugando, y con
            // monitores, audio y luces la zapatilla pica cerca de 880 W. Sigue siendo el 40%
            // del limite de 10A del enchufe.
            SimulatedDevice.Plug("plug-fede", totalStart,
                new RandomWalkProfile(idleWatts: 20, minWatts: 10, maxWatts: 880, spikeChance: 0.012))
        ];

        // Lo que no pasa por ninguna zapatilla: heladera, lavarropas, cocina, el resto de las
        // luces. Sube de tarde y de noche.
        var baseline = new HouseholdBaselineProfile(nightWatts: 70, dayWatts: 98, eveningWatts: 165);

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
