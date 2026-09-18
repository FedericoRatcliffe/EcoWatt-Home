using System.Buffers;
using System.Text;
using EcoWattCasa.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace EcoWattCasa.Infrastructure.Mqtt;

/// <summary>
/// Escucha tele/+/SENSOR y le pasa cada mensaje al servicio de ingesta. Reintenta la conexion
/// para siempre: el broker puede arrancar despues que la API (o caerse) y el sistema tiene que
/// reengancharse solo.
/// </summary>
public sealed class MqttListenerService(
    MqttConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<MqttOptions> options,
    ILogger<MqttListenerService> logger) : BackgroundService
{
    private readonly MqttOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        connection.Client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        connection.Client.DisconnectedAsync += e =>
        {
            logger.LogWarning("Desconectado del broker MQTT: {Reason}", e.Reason);
            return Task.CompletedTask;
        };

        var delay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Solo se (re)suscribe al conectar: con sesion limpia la suscripcion se pierde
                // en cada reconexion, pero repetirla mientras la conexion sigue viva no aporta.
                if (!connection.IsConnected && await connection.EnsureConnectedAsync(stoppingToken))
                    await SubscribeAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo el ciclo de conexion MQTT, se reintenta en {Delay}s.", delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SubscribeAsync(CancellationToken ct)
    {
        // Re-suscribirse es idempotente y hace falta despues de cada reconexion con sesion limpia.
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic(_options.TelemetryTopicFilter)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .WithTopicFilter(f => f
                .WithTopic(_options.RelayStateTopicFilter)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        await connection.Client.SubscribeAsync(subscribeOptions, ct);
        logger.LogInformation(
            "Suscrito a {Telemetry} y {RelayState}", _options.TelemetryTopicFilter, _options.RelayStateTopicFilter);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var routed = Route(topic);
        if (routed is null)
        {
            logger.LogDebug("Topic inesperado, se ignora: {Topic}", topic);
            return;
        }

        var (kind, deviceTopic) = routed.Value;

        try
        {
            // Payload es ReadOnlySequence<byte>: el caso normal es un solo segmento y no aloca.
            var payload = e.ApplicationMessage.Payload;
            var text = payload.IsSingleSegment
                ? Encoding.UTF8.GetString(payload.FirstSpan)
                : Encoding.UTF8.GetString(payload.ToArray());

            // El handler corre en el hilo del cliente MQTT: se abre un scope propio para
            // no compartir el DbContext entre mensajes.
            await using var scope = scopeFactory.CreateAsyncScope();

            if (kind == TopicKind.Telemetry)
                await scope.ServiceProvider.GetRequiredService<EnergyIngestionService>().IngestAsync(deviceTopic, text);
            else
                await scope.ServiceProvider.GetRequiredService<RelayStateService>().ApplyAsync(deviceTopic, text);
        }
        catch (Exception ex)
        {
            // Tragar la excepcion a proposito: si esto burbujea, MQTTnet corta la conexion
            // y se pierde la telemetria de todos los dispositivos por un mensaje malo.
            logger.LogError(ex, "Error procesando mensaje de {Topic}", topic);
        }
    }

    internal enum TopicKind
    {
        /// <summary>tele/{topic}/SENSOR: la telemetria de energia.</summary>
        Telemetry,

        /// <summary>stat/{topic}/POWER: el estado del rele que confirma el equipo.</summary>
        RelayState
    }

    /// <summary>
    /// De "tele/plug-pc/SENSOR" saca (Telemetry, "plug-pc"); de "stat/plug-pc/POWER" saca
    /// (RelayState, "plug-pc"). null para cualquier otra cosa.
    ///
    /// Tasmota tambien publica stat/{topic}/RESULT con el mismo dato en JSON; se ignora a
    /// proposito para no procesar el mismo cambio dos veces.
    /// </summary>
    internal static (TopicKind Kind, string DeviceTopic)? Route(string topic)
    {
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;

        return (parts[0], parts[2]) switch
        {
            ("tele", "SENSOR") => (TopicKind.Telemetry, parts[1]),
            ("stat", "POWER") => (TopicKind.RelayState, parts[1]),
            _ => null
        };
    }
}
