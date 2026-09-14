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
            .Build();

        await connection.Client.SubscribeAsync(subscribeOptions, ct);
        logger.LogInformation("Suscrito a {Filter}", _options.TelemetryTopicFilter);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var deviceTopic = ExtractDeviceTopic(topic);
        if (deviceTopic is null)
        {
            logger.LogDebug("Topic inesperado, se ignora: {Topic}", topic);
            return;
        }

        try
        {
            // Payload es ReadOnlySequence<byte>: el caso normal es un solo segmento y no aloca.
            var payload = e.ApplicationMessage.Payload;
            var json = payload.IsSingleSegment
                ? Encoding.UTF8.GetString(payload.FirstSpan)
                : Encoding.UTF8.GetString(payload.ToArray());

            // El handler corre en el hilo del cliente MQTT: se abre un scope propio para
            // no compartir el DbContext entre mensajes.
            await using var scope = scopeFactory.CreateAsyncScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<EnergyIngestionService>();
            await ingestion.IngestAsync(deviceTopic, json);
        }
        catch (Exception ex)
        {
            // Tragar la excepcion a proposito: si esto burbujea, MQTTnet corta la conexion
            // y se pierde la telemetria de todos los dispositivos por un mensaje malo.
            logger.LogError(ex, "Error procesando mensaje de {Topic}", topic);
        }
    }

    /// <summary>De "tele/sonoff-pc/SENSOR" saca "sonoff-pc".</summary>
    internal static string? ExtractDeviceTopic(string topic)
    {
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && parts[0] == "tele" && parts[2] == "SENSOR" ? parts[1] : null;
    }
}
