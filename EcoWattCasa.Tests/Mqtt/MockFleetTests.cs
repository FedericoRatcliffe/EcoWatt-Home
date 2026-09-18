using EcoWattCasa.Application.Mqtt;
using EcoWattCasa.Infrastructure.Persistence;
using EcoWattCasa.MockDevices;

namespace EcoWattCasa.Tests.Mqtt;

/// <summary>
/// El simulador contra el parser real.
///
/// Sin hardware, el mock es la unica fuente de payloads del sistema, asi que si publica algo
/// que la ingesta no sabe leer, el error no aparece hasta que llegan los equipos. Estos tests
/// cierran ese circuito: se simula, se serializa como Tasmota y se parsea con el mismo codigo
/// que corre en produccion.
/// </summary>
public class MockFleetTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Corre la flota unos pasos y devuelve el ultimo payload de cada equipo, ya parseado.</summary>
    private static Dictionary<string, TasmotaEnergy> Run(int steps = 20, int minutesPerStep = 1)
    {
        var fleet = Fleet.Build();
        var rng = new Random(1234);
        var step = TimeSpan.FromMinutes(minutesPerStep);
        var parsed = new Dictionary<string, TasmotaEnergy>();

        for (var i = 0; i < steps; i++)
        {
            foreach (var (device, sample) in fleet.Step(step, Start.AddMinutes(i * minutesPerStep), rng))
            {
                var json = device.ToTasmotaJson(sample);
                parsed[device.Topic] = TasmotaSensorPayload.Parse(json)?.Energy
                    ?? throw new InvalidOperationException($"{device.Topic} publico algo que el parser no entiende: {json}");
            }
        }

        return parsed;
    }

    // ---------- La flota es la que se compro ----------

    [Fact]
    public void Se_simulan_los_cinco_equipos_comprados()
    {
        var fleet = Fleet.Build();

        Assert.Equal(5, fleet.All.Length);
        Assert.Equal(4, fleet.Plugs.Length);
        Assert.Equal("em2-tablero", fleet.Meter.Topic);
    }

    [Fact]
    public void Los_topics_simulados_son_los_que_siembra_la_base()
    {
        // Si se separan, el mock publica en topics que no corresponden a ningun dispositivo
        // y la ingesta empieza a dar de alta equipos fantasma.
        var simulated = Fleet.Build().All.Select(d => d.Topic).OrderBy(t => t);
        var seeded = DbSeeder.Devices.Select(d => d.Topic).OrderBy(t => t);

        Assert.Equal(seeded, simulated);
    }

    [Fact]
    public void Solo_el_medidor_de_tablero_no_tiene_rele()
    {
        var fleet = Fleet.Build();

        Assert.False(fleet.Meter.HasRelay);
        Assert.All(fleet.Plugs, p => Assert.True(p.HasRelay));
    }

    // ---------- Lo que publica se puede leer ----------

    [Fact]
    public void Todo_lo_que_publica_el_mock_lo_entiende_el_parser()
    {
        // Run() tira si algun payload no parsea.
        var parsed = Run();

        Assert.Equal(5, parsed.Count);
    }

    [Fact]
    public void El_medidor_publica_dos_canales_como_con_SO129()
    {
        var energy = Run()["em2-tablero"];

        Assert.True(energy.IsMultiChannel);
        Assert.Equal(2, energy.ChannelCount);
        Assert.True(energy.Power.IsArray);
    }

    [Fact]
    public void El_medidor_publica_la_tension_escalar_aunque_parta_la_energia()
    {
        // El EM2 tiene dos canales de corriente pero uno solo de tension. Es justo la mezcla
        // que motiva que el parser decida la forma campo por campo.
        var energy = Run()["em2-tablero"];

        Assert.False(energy.Voltage.IsArray);
        Assert.InRange(energy.ForChannel(0).Voltage!.Value, 210, 230);
        Assert.Equal(energy.ForChannel(0).Voltage, energy.ForChannel(1).Voltage);
    }

    [Fact]
    public void El_canal_sin_pinza_del_medidor_queda_en_cero()
    {
        // Se compro una sola pinza CT, asi que el segundo canal no mide nada.
        var energy = Run()["em2-tablero"];

        Assert.Equal(0d, energy.ForChannel(1).Power);
        Assert.Equal(0d, energy.ForChannel(1).Total);
    }

    [Fact]
    public void Los_enchufes_publican_un_solo_canal_escalar()
    {
        var parsed = Run();

        foreach (var topic in new[] { "plug-heladera", "plug-pc", "plug-lavarropas", "plug-libre" })
        {
            Assert.False(parsed[topic].IsMultiChannel);
            Assert.False(parsed[topic].Power.IsArray);
        }
    }

    // ---------- La cuenta que va a mostrar el dashboard ----------

    [Fact]
    public void El_total_del_tablero_nunca_queda_por_debajo_de_los_enchufes()
    {
        // El "consumo no identificado" es total menos enchufes. Si el mock lo dejara dar
        // negativo, el dashboard mostraria un numero imposible y no se sabria si el error
        // esta en la cuenta o en los datos.
        var fleet = Fleet.Build();
        var rng = new Random(99);
        var step = TimeSpan.FromMinutes(5);

        // 24 h completas: cubre la curva de la linea de base de noche, dia y tarde.
        for (var i = 0; i < 288; i++)
        {
            var samples = fleet.Step(step, Start.AddMinutes(i * 5), rng);
            var plugs = samples.Where(s => s.Device.HasRelay).Sum(s => s.Sample.Watts);
            var house = samples.Single(s => !s.Device.HasRelay).Sample.Watts;

            Assert.True(house >= plugs, $"paso {i}: la casa midio {house:0.0} W y los enchufes {plugs:0.0} W");
        }
    }

    [Fact]
    public void El_consumo_simulado_se_parece_al_de_las_facturas()
    {
        // Las facturas reales dan ~175 kWh/mes, o sea 243 W promedio; el mock da ~247. La
        // banda es de +-15% a proposito: no busca clavar un numero, busca que un dispositivo
        // que deje de sumar o un perfil con un cero de mas se note.
        var fleet = Fleet.Build();
        var rng = new Random(7);
        var step = TimeSpan.FromMinutes(5);
        var sum = 0d;
        const int steps = 288;

        for (var i = 0; i < steps; i++)
            sum += fleet.Step(step, Start.AddMinutes(i * 5), rng).Single(s => !s.Device.HasRelay).Sample.Watts;

        Assert.InRange(sum / steps, 207, 279);
    }

    [Fact]
    public void Apagar_un_enchufe_baja_el_total_de_la_casa()
    {
        // El medidor mide la suma, asi que cortar un enchufe tiene que verse en el tablero.
        // Es la unica forma de probar sin hardware que la cuenta esta encadenada.
        var fleet = Fleet.Build();
        var rng = new Random(42);
        var step = TimeSpan.FromMinutes(1);
        var pc = fleet.Plugs.Single(p => p.Topic == "plug-pc");

        double House(int i) => fleet.Step(step, Start.AddMinutes(i), rng).Single(s => !s.Device.HasRelay).Sample.Watts;

        // Se deja estabilizar con la PC prendida y se promedia, porque el perfil se mueve solo.
        var on = Enumerable.Range(0, 30).Select(House).Average();

        pc.RelayOn = false;
        var off = Enumerable.Range(30, 30).Select(House).Average();

        Assert.True(off < on, $"con la PC prendida {on:0.0} W, apagada {off:0.0} W");
    }

    [Fact]
    public void El_medidor_no_se_apaga_aunque_le_manden_el_comando()
    {
        // No tiene rele: RelayOn no deberia tener efecto sobre lo que mide.
        var fleet = Fleet.Build();
        var rng = new Random(5);

        fleet.Meter.RelayOn = false;
        var watts = fleet.Step(TimeSpan.FromMinutes(1), Start, rng).Single(s => !s.Device.HasRelay).Sample.Watts;

        Assert.True(watts > 0);
    }

    // ---------- El contador acumulado ----------

    [Fact]
    public void El_contador_acumulado_solo_sube()
    {
        // Total es la fuente de verdad del consumo: si bajara, la ingesta lo leeria como un
        // reset del equipo y perderia el tramo.
        var fleet = Fleet.Build();
        var rng = new Random(3);
        var previous = new Dictionary<string, double>();

        for (var i = 0; i < 60; i++)
        {
            foreach (var (device, sample) in fleet.Step(TimeSpan.FromMinutes(1), Start.AddMinutes(i), rng))
            {
                var total = sample.Channels.Sum(c => c.TotalKwh);
                if (previous.TryGetValue(device.Topic, out var before))
                    Assert.True(total >= before, $"{device.Topic} retrocedio de {before} a {total} kWh");

                previous[device.Topic] = total;
            }
        }
    }

    [Fact]
    public void El_enchufe_sin_nada_conectado_mide_cero()
    {
        var energy = Run()["plug-libre"];

        Assert.Equal(0d, energy.ForChannel(0).Power);
        Assert.Equal(0d, energy.ForChannel(0).Total);
    }
}
