using System.Text.Json;
using System.Text.Json.Serialization;

namespace EcoWattCasa.Application.Mqtt;

/// <summary>Payload de tele/{topic}/SENSOR tal como lo publica Tasmota.</summary>
public sealed class TasmotaSensorPayload
{
    [JsonPropertyName("Time")]
    public string? Time { get; set; }

    [JsonPropertyName("ENERGY")]
    public TasmotaEnergy? Energy { get; set; }

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static TasmotaSensorPayload? Parse(string json)
        => JsonSerializer.Deserialize<TasmotaSensorPayload>(json, SerializerOptions);
}

public sealed class TasmotaEnergy
{
    [JsonPropertyName("TotalStartTime")]
    public string? TotalStartTime { get; set; }

    /// <summary>kWh acumulados desde TotalStartTime.</summary>
    [JsonPropertyName("Total")]
    public double? Total { get; set; }

    [JsonPropertyName("Yesterday")]
    public double? Yesterday { get; set; }

    [JsonPropertyName("Today")]
    public double? Today { get; set; }

    /// <summary>Potencia activa en W.</summary>
    [JsonPropertyName("Power")]
    public double? Power { get; set; }

    [JsonPropertyName("ApparentPower")]
    public double? ApparentPower { get; set; }

    [JsonPropertyName("ReactivePower")]
    public double? ReactivePower { get; set; }

    [JsonPropertyName("Factor")]
    public double? Factor { get; set; }

    [JsonPropertyName("Voltage")]
    public double? Voltage { get; set; }

    [JsonPropertyName("Current")]
    public double? Current { get; set; }
}
