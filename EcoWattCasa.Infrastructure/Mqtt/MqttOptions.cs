namespace EcoWattCasa.Infrastructure.Mqtt;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    /// <summary>
    /// true = la API levanta su propio broker en <see cref="Port"/>, sin depender de un
    /// Mosquitto instalado. false = se conecta a uno externo en <see cref="Host"/>.
    /// </summary>
    public bool Embedded { get; set; }

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = "ecowatt-api";
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Wildcard de telemetria de Tasmota. Un nivel + por dispositivo.</summary>
    public string TelemetryTopicFilter { get; set; } = "tele/+/SENSOR";

    /// <summary>Segundos entre reintentos cuando el broker no esta disponible.</summary>
    public int ReconnectDelaySeconds { get; set; } = 5;

    public int KeepAliveSeconds { get; set; } = 30;
}
