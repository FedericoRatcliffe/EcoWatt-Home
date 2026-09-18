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

/// <summary>
/// Bloque ENERGY. Cada campo puede venir escalar (enchufes, un canal) o como array por canal
/// (el EM2 con <c>SO129 1</c>), asi que todos son <see cref="EnergyValue"/>.
/// </summary>
public sealed class TasmotaEnergy
{
    [JsonPropertyName("TotalStartTime")]
    public string? TotalStartTime { get; set; }

    /// <summary>kWh acumulados desde TotalStartTime. Es la fuente de verdad del consumo.</summary>
    [JsonPropertyName("Total")]
    public EnergyValue Total { get; set; }

    [JsonPropertyName("Yesterday")]
    public EnergyValue Yesterday { get; set; }

    [JsonPropertyName("Today")]
    public EnergyValue Today { get; set; }

    /// <summary>Potencia activa (real) en W.</summary>
    [JsonPropertyName("Power")]
    public EnergyValue Power { get; set; }

    [JsonPropertyName("ApparentPower")]
    public EnergyValue ApparentPower { get; set; }

    [JsonPropertyName("ReactivePower")]
    public EnergyValue ReactivePower { get; set; }

    [JsonPropertyName("Factor")]
    public EnergyValue Factor { get; set; }

    [JsonPropertyName("Voltage")]
    public EnergyValue Voltage { get; set; }

    [JsonPropertyName("Current")]
    public EnergyValue Current { get; set; }

    /// <summary>
    /// Canales que trajo el payload: el maximo entre los campos que vinieron como array.
    /// Un enchufe da 1; el EM2 con SO129 deberia dar 2.
    /// </summary>
    public int ChannelCount
    {
        get
        {
            var channels = 1;
            foreach (var value in new[] { Total, Today, Power, ApparentPower, ReactivePower, Factor, Current })
            {
                if (value.IsArray && value.ChannelCount > channels)
                    channels = value.ChannelCount;
            }

            return channels;
        }
    }

    /// <summary>Algun campo vino como array, o sea que el dispositivo publica por canal.</summary>
    public bool IsMultiChannel => ChannelCount > 1;

    /// <summary>Resuelve el bloque a los valores de un canal concreto.</summary>
    public ChannelEnergy ForChannel(int channelIndex) => new(
        Total: Total.Channel(channelIndex),
        Today: Today.Channel(channelIndex),
        Yesterday: Yesterday.Channel(channelIndex),
        Power: Power.Channel(channelIndex),
        ApparentPower: ApparentPower.Channel(channelIndex),
        ReactivePower: ReactivePower.Channel(channelIndex),
        Factor: Factor.Channel(channelIndex),
        Voltage: Voltage.Channel(channelIndex),
        Current: Current.Channel(channelIndex));
}

/// <summary>
/// El bloque ENERGY ya resuelto a un canal. Es lo que consume la ingesta: a esta altura no
/// importa si el dispositivo era multicanal.
/// </summary>
public sealed record ChannelEnergy(
    double? Total,
    double? Today,
    double? Yesterday,
    double? Power,
    double? ApparentPower,
    double? ReactivePower,
    double? Factor,
    double? Voltage,
    double? Current)
{
    /// <summary>
    /// El canal pedido no trajo ningun dato. Pasa cuando la configuracion apunta a un canal
    /// que el dispositivo no publica: hay que avisar, no medir cero en silencio.
    /// </summary>
    public bool IsEmpty => Power is null && Total is null && Current is null;
}
