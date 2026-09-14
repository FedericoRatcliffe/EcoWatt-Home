using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace EcoWattCasa.Infrastructure.Mqtt;

/// <summary>
/// Duena del unico cliente MQTT del proceso. La comparten el listener de telemetria y el
/// publisher de comandos, para no abrir dos conexiones al broker por la misma casa.
/// </summary>
public sealed class MqttConnection : IAsyncDisposable
{
    private readonly MqttOptions _options;
    private readonly ILogger<MqttConnection> _logger;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MqttConnection(IOptions<MqttOptions> options, ILogger<MqttConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
    }

    public IMqttClient Client => _client;

    public bool IsConnected => _client.IsConnected;

    /// <summary>Conecta si hace falta. Devuelve false si el broker no responde.</summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_client.IsConnected)
            return true;

        await _gate.WaitAsync(ct);
        try
        {
            if (_client.IsConnected)
                return true;

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.Host, _options.Port)
                .WithClientId(_options.ClientId)
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds));

            if (!string.IsNullOrWhiteSpace(_options.Username))
                builder = builder.WithCredentials(_options.Username, _options.Password ?? string.Empty);

            await _client.ConnectAsync(builder.Build(), ct);
            _logger.LogInformation("Conectado al broker MQTT {Host}:{Port} como {ClientId}",
                _options.Host, _options.Port, _options.ClientId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("No se pudo conectar a MQTT {Host}:{Port}: {Message}",
                _options.Host, _options.Port, ex.Message);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Publica un comando. Si el broker esta caido intenta reconectar una vez.</summary>
    public async Task PublishAsync(string topic, string payload, CancellationToken ct = default)
    {
        if (!await EnsureConnectedAsync(ct))
            throw new InvalidOperationException($"Sin conexion al broker MQTT en {_options.Host}:{_options.Port}.");

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.PublishAsync(message, ct);
        _logger.LogInformation("MQTT -> {Topic}: {Payload}", topic, payload);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error al desconectar el cliente MQTT.");
        }

        _client.Dispose();
        _gate.Dispose();
    }
}
