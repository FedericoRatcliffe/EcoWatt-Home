using EcoWattCasa.Application.Mqtt;

namespace EcoWattCasa.Tests.Mqtt;

/// <summary>
/// Lectura del bloque ENERGY de Tasmota.
///
/// El punto del ejercicio: el mismo parser tiene que servir para los enchufes, que publican
/// escalares, y para el EM2, que con <c>SO129 1</c> publica la energia por canal. Y como el
/// EM2 tiene dos canales de corriente pero uno solo de tension, lo esperable es que en el
/// MISMO payload convivan campos escalares y campos array.
///
/// Ningun payload real del EM2 esta publicado en ningun lado, asi que el formato exacto se va
/// a confirmar cuando llegue el equipo. Estos casos cubren las dos formas y la mezcla.
/// </summary>
public class TasmotaPayloadTests
{
    /// <summary>Payload de un enchufe: un solo canal, todo escalar.</summary>
    private const string PlugJson = """
        {
          "Time": "2026-09-17T18:15:00",
          "ENERGY": {
            "TotalStartTime": "2026-01-01T00:00:00",
            "Total": 12.345,
            "Yesterday": 0.456,
            "Today": 0.123,
            "Power": 215,
            "ApparentPower": 220,
            "ReactivePower": 45,
            "Factor": 0.97,
            "Voltage": 224,
            "Current": 0.982
          }
        }
        """;

    /// <summary>
    /// Payload esperado del EM2: dos canales de corriente y uno de tension, con una sola
    /// pinza CT instalada (el canal 1 reporta cero).
    /// </summary>
    private const string MeterJson = """
        {
          "Time": "2026-09-17T18:15:00",
          "ENERGY": {
            "TotalStartTime": "2026-01-01T00:00:00",
            "Total": [842.117, 0.000],
            "Yesterday": [5.832, 0.000],
            "Today": [3.204, 0.000],
            "Power": [612, 0],
            "ApparentPower": [648, 0],
            "ReactivePower": [212, 0],
            "Factor": [0.94, 0.00],
            "Voltage": 224,
            "Current": [2.893, 0.000]
          }
        }
        """;

    private static TasmotaEnergy Energy(string json) =>
        TasmotaSensorPayload.Parse(json)?.Energy ?? throw new InvalidOperationException("no parseo");

    // ---------- Enchufe: todo escalar ----------

    [Fact]
    public void Un_enchufe_publica_un_solo_canal()
    {
        var energy = Energy(PlugJson);

        Assert.Equal(1, energy.ChannelCount);
        Assert.False(energy.IsMultiChannel);
    }

    [Fact]
    public void Los_valores_escalares_se_leen_en_el_canal_cero()
    {
        var e = Energy(PlugJson).ForChannel(0);

        Assert.Equal(12.345, e.Total);
        Assert.Equal(215, e.Power);
        Assert.Equal(224, e.Voltage);
        Assert.Equal(0.982, e.Current);
        Assert.Equal(0.97, e.Factor);
        Assert.Equal(0.123, e.Today);
    }

    [Fact]
    public void Un_escalar_responde_para_cualquier_canal()
    {
        // No es un descuido: la tension del EM2 es una sola y aplica a los dos canales de
        // corriente, asi que un escalar tiene que responder igual para el canal 1.
        var e = Energy(PlugJson).ForChannel(1);

        Assert.Equal(215, e.Power);
        Assert.Equal(224, e.Voltage);
    }

    // ---------- Medidor: arrays y mezcla ----------

    [Fact]
    public void El_medidor_publica_dos_canales()
    {
        var energy = Energy(MeterJson);

        Assert.Equal(2, energy.ChannelCount);
        Assert.True(energy.IsMultiChannel);
    }

    [Fact]
    public void El_canal_cero_del_medidor_es_el_que_tiene_la_pinza()
    {
        var e = Energy(MeterJson).ForChannel(0);

        Assert.Equal(842.117, e.Total);
        Assert.Equal(612, e.Power);
        Assert.Equal(2.893, e.Current);
        Assert.Equal(0.94, e.Factor);
    }

    [Fact]
    public void El_canal_sin_pinza_reporta_cero_y_no_null()
    {
        // Cero es un dato: dice que ese canal existe y no esta midiendo. Distinto de ausente.
        var e = Energy(MeterJson).ForChannel(1);

        Assert.Equal(0d, e.Power);
        Assert.Equal(0d, e.Total);
    }

    [Fact]
    public void La_tension_escalar_convive_con_las_potencias_por_canal()
    {
        // El caso que motiva todo el diseno: un canal de tension, dos de corriente.
        var energy = Energy(MeterJson);

        Assert.False(energy.Voltage.IsArray);
        Assert.True(energy.Power.IsArray);

        Assert.Equal(224, energy.ForChannel(0).Voltage);
        Assert.Equal(224, energy.ForChannel(1).Voltage);
        Assert.NotEqual(energy.ForChannel(0).Power, energy.ForChannel(1).Power);
    }

    [Fact]
    public void La_suma_de_canales_reconstruye_lo_que_publicaria_Tasmota_sin_SO129()
    {
        // Sin SO129 Tasmota suma los canales en un escalar. Teniendo los dos, se puede
        // reconstruir ese numero, que es util para contrastar contra la pantalla del equipo.
        Assert.Equal(612d, Energy(MeterJson).Power.Sum);
        Assert.Equal(842.117, Energy(MeterJson).Total.Sum!.Value, precision: 3);
    }

    // ---------- Canal mal configurado ----------

    [Fact]
    public void Pedir_un_canal_que_no_existe_no_devuelve_el_equivocado()
    {
        // Si la configuracion apunta al canal 5 de un equipo de 2, es preferible quedarse sin
        // dato y avisar antes que medir en silencio el canal que no era.
        var e = Energy(MeterJson).ForChannel(5);

        Assert.Null(e.Power);
        Assert.Null(e.Total);
        Assert.True(e.IsEmpty);
    }

    [Fact]
    public void Un_canal_valido_nunca_queda_marcado_como_vacio()
    {
        Assert.False(Energy(MeterJson).ForChannel(0).IsEmpty);
        Assert.False(Energy(MeterJson).ForChannel(1).IsEmpty);
        Assert.False(Energy(PlugJson).ForChannel(0).IsEmpty);
    }

    // ---------- Formas raras que manda Tasmota ----------

    [Fact]
    public void Acepta_numeros_entre_comillas()
    {
        // Algunas versiones de Tasmota mandan los valores como string.
        var energy = Energy("""{"ENERGY":{"Power":"215.5","Total":"12.3"}}""");

        Assert.Equal(215.5, energy.ForChannel(0).Power);
        Assert.Equal(12.3, energy.ForChannel(0).Total);
    }

    [Fact]
    public void Acepta_arrays_con_numeros_entre_comillas()
    {
        var energy = Energy("""{"ENERGY":{"Power":["612","0"]}}""");

        Assert.True(energy.Power.IsArray);
        Assert.Equal(612d, energy.ForChannel(0).Power);
        Assert.Equal(0d, energy.ForChannel(1).Power);
    }

    [Fact]
    public void Un_null_en_un_array_no_corre_los_indices_de_los_demas()
    {
        // Si un canal viene null, ponerlo en cero mantiene alineados los indices; saltearlo
        // haria que el canal 2 se lea como canal 1.
        var energy = Energy("""{"ENERGY":{"Power":[100,null,300]}}""");

        Assert.Equal(100d, energy.ForChannel(0).Power);
        Assert.Equal(0d, energy.ForChannel(1).Power);
        Assert.Equal(300d, energy.ForChannel(2).Power);
    }

    [Fact]
    public void Un_campo_ausente_queda_en_null_y_no_en_cero()
    {
        // Distinguir "no lo mando" de "midio cero" importa: el calculo de energia decide
        // entre usar el contador o integrar segun si Total vino o no.
        var energy = Energy("""{"ENERGY":{"Power":215}}""");

        Assert.Null(energy.ForChannel(0).Total);
        Assert.False(energy.Total.HasValue);
        Assert.Equal(215d, energy.ForChannel(0).Power);
    }

    [Fact]
    public void Un_campo_con_null_explicito_tambien_queda_ausente()
    {
        var energy = Energy("""{"ENERGY":{"Power":215,"Total":null}}""");

        Assert.Null(energy.ForChannel(0).Total);
    }

    [Fact]
    public void Un_array_vacio_se_trata_como_ausente()
    {
        var energy = Energy("""{"ENERGY":{"Power":[]}}""");

        Assert.False(energy.Power.HasValue);
        Assert.Null(energy.ForChannel(0).Power);
    }

    [Fact]
    public void Un_mensaje_sin_bloque_ENERGY_no_rompe()
    {
        // Tasmota publica STATE y otros sensores en el mismo topic.
        var payload = TasmotaSensorPayload.Parse("""{"Time":"2026-09-17T18:15:00","DS18B20":{"Temperature":21.4}}""");

        Assert.NotNull(payload);
        Assert.Null(payload.Energy);
    }

    [Fact]
    public void Se_conserva_el_inicio_del_contador()
    {
        // TotalStartTime dice desde cuando acumula Total: si cambia, el contador se reseteo.
        Assert.Equal("2026-01-01T00:00:00", Energy(MeterJson).TotalStartTime);
    }
}
