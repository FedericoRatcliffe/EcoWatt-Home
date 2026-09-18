using EcoWattCasa.Infrastructure.Mqtt;

namespace EcoWattCasa.Tests.Mqtt;

/// <summary>
/// El ruteo de topics: decide si un mensaje es telemetria, estado de rele, o se tira.
///
/// Un error aca se ve como "el sistema no anda" sin ningun error en los logs, porque el
/// mensaje simplemente se descarta en silencio.
/// </summary>
public class TopicRoutingTests
{
    [Fact]
    public void La_telemetria_va_a_la_ingesta()
    {
        var routed = MqttListenerService.Route("tele/plug-pc/SENSOR");

        Assert.Equal(MqttListenerService.TopicKind.Telemetry, routed?.Kind);
        Assert.Equal("plug-pc", routed?.DeviceTopic);
    }

    [Fact]
    public void El_estado_del_rele_va_al_servicio_de_estado()
    {
        var routed = MqttListenerService.Route("stat/plug-heladera/POWER");

        Assert.Equal(MqttListenerService.TopicKind.RelayState, routed?.Kind);
        Assert.Equal("plug-heladera", routed?.DeviceTopic);
    }

    [Fact]
    public void El_RESULT_de_Tasmota_se_ignora()
    {
        // Trae el mismo cambio que stat/POWER pero en JSON: procesar los dos aplicaria el
        // mismo estado dos veces.
        Assert.Null(MqttListenerService.Route("stat/plug-pc/RESULT"));
    }

    [Theory]
    [InlineData("cmnd/plug-pc/POWER")]         // lo que publica el propio backend
    [InlineData("tele/plug-pc/STATE")]         // telemetria de wifi y uptime, sin ENERGY
    [InlineData("tele/plug-pc/LWT")]           // online/offline
    [InlineData("tele/plug-pc")]               // incompleto
    [InlineData("tele/casa/plug-pc/SENSOR")]   // un nivel de mas
    [InlineData("")]
    public void Lo_que_no_corresponde_no_se_rutea(string topic)
    {
        Assert.Null(MqttListenerService.Route(topic));
    }

    [Fact]
    public void Un_topic_con_barras_de_mas_igual_se_resuelve()
    {
        // Algunos brokers reenvian con barra inicial.
        var routed = MqttListenerService.Route("/tele/plug-pc/SENSOR");

        Assert.Equal(MqttListenerService.TopicKind.Telemetry, routed?.Kind);
    }
}
