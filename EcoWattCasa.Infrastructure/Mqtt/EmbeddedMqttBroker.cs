using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace EcoWattCasa.Infrastructure.Mqtt;

/// <summary>
/// Broker MQTT corriendo dentro de la propia API.
///
/// Para una casa con unos pocos enchufes no hace falta un Mosquitto aparte: el broker vive en
/// el mismo proceso, arranca y para con la API, y los Sonoff se conectan igual apuntando a la
/// IP de esta maquina. Escucha en todas las interfaces, no solo en localhost, porque los
/// dispositivos estan en la LAN.
///
/// Se apaga con "Mqtt:Embedded": false para usar un Mosquitto externo.
/// </summary>
public sealed class EmbeddedMqttBroker(
    IOptions<MqttOptions> options,
    ILogger<EmbeddedMqttBroker> logger) : IHostedService, IAsyncDisposable
{
    private readonly MqttOptions _options = options.Value;
    private MqttServer? _server;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(_options.Port)
            .WithDefaultEndpointBoundIPAddress(IPAddress.Any)
            .Build();

        _server = new MqttServerFactory().CreateMqttServer(serverOptions);

        _server.ValidatingConnectionAsync += ValidateAsync;

        _server.ClientConnectedAsync += e =>
        {
            logger.LogInformation("MQTT: se conecto {ClientId} desde {Endpoint}", e.ClientId, e.RemoteEndPoint);
            return Task.CompletedTask;
        };

        _server.ClientDisconnectedAsync += e =>
        {
            logger.LogInformation("MQTT: se desconecto {ClientId} ({Reason})", e.ClientId, e.DisconnectType);
            return Task.CompletedTask;
        };

        try
        {
            await _server.StartAsync();
            logger.LogInformation(
                "Broker MQTT embebido escuchando en 0.0.0.0:{Port}. Configura MqttHost en cada equipo Tasmota con la IP de esta PC.",
                _options.Port);
        }
        catch (Exception ex)
        {
            // Lo mas probable: ya hay un Mosquitto ocupando el puerto. No es motivo para tumbar
            // la API; el listener se va a conectar a ese broker igual.
            logger.LogWarning(ex,
                "No se pudo levantar el broker embebido en el puerto {Port}. " +
                "Si ya hay un broker corriendo ahi, pone \"Mqtt:Embedded\": false para no intentarlo.",
                _options.Port);

            await DisposeServerAsync();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is { IsStarted: true })
        {
            await _server.StopAsync();
            logger.LogInformation("Broker MQTT embebido detenido.");
        }

        await DisposeServerAsync();
    }

    /// <summary>
    /// Si hay credenciales configuradas se exigen; si no, se acepta cualquier cliente. La
    /// segunda opcion es la razonable en una LAN domestica y es lo que hace Mosquitto con
    /// allow_anonymous true.
    /// </summary>
    private Task ValidateAsync(ValidatingConnectionEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_options.Username))
            return Task.CompletedTask;

        var ok = e.UserName == _options.Username && e.Password == (_options.Password ?? string.Empty);
        if (!ok)
        {
            e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            logger.LogWarning("MQTT: rechazado {ClientId}, credenciales invalidas", e.ClientId);
        }

        return Task.CompletedTask;
    }

    private ValueTask DisposeServerAsync()
    {
        _server?.Dispose();
        _server = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => DisposeServerAsync();
}
