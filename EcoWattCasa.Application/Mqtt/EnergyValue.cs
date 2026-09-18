using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EcoWattCasa.Application.Mqtt;

/// <summary>
/// Un campo del bloque ENERGY de Tasmota, que puede venir como escalar o como array por canal.
///
/// Los enchufes tienen un solo canal y publican escalares. El EM2 tiene dos canales de
/// corriente y con <c>SO129 1</c> publica la energia por canal en vez de la suma, o sea arrays.
/// Pero como tiene un solo canal de tension, lo mas probable es que en el MISMO payload venga
/// <c>Voltage</c> escalar y <c>Power</c>/<c>Total</c> como arrays. Por eso la forma se decide
/// por campo y no por dispositivo.
///
/// Un escalar responde lo mismo para cualquier canal que se le pida, que es justo lo correcto
/// para la tension: una sola medicion que aplica a los dos canales de corriente.
/// </summary>
[JsonConverter(typeof(EnergyValueConverter))]
public readonly struct EnergyValue
{
    private readonly double[]? _values;

    private EnergyValue(double[]? values, bool isArray)
    {
        _values = values;
        IsArray = isArray;
    }

    /// <summary>Vino como array en el JSON. Sirve para detectar un dispositivo multicanal.</summary>
    public bool IsArray { get; }

    public bool HasValue => _values is { Length: > 0 };

    /// <summary>Cuantos canales trajo. 1 para un escalar, 0 si el campo no vino.</summary>
    public int ChannelCount => _values?.Length ?? 0;

    public static EnergyValue Scalar(double value) => new([value], isArray: false);

    public static EnergyValue Array(params double[] values) => new(values, isArray: true);

    public static EnergyValue Missing => default;

    /// <summary>
    /// Valor del canal pedido. Un escalar responde para cualquier indice; un array devuelve
    /// null si el indice se pasa de los canales que trajo, para que una configuracion mal
    /// puesta se note en vez de leer el canal equivocado.
    /// </summary>
    public double? Channel(int index)
    {
        if (_values is not { Length: > 0 } values)
            return null;

        if (!IsArray)
            return values[0];

        return index >= 0 && index < values.Length ? values[index] : null;
    }

    /// <summary>Suma de todos los canales. Es lo que publica Tasmota cuando SO129 esta apagado.</summary>
    public double? Sum => _values is { Length: > 0 } values ? values.Sum() : null;

    public override string ToString()
        => _values is not { Length: > 0 } values
            ? "(vacio)"
            : IsArray
                ? $"[{string.Join(", ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}]"
                : values[0].ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Lee un campo de ENERGY sin saber de antemano si viene escalar o como array.
/// Acepta tambien numeros entre comillas, que es como los manda Tasmota en algunas versiones.
/// </summary>
public sealed class EnergyValueConverter : JsonConverter<EnergyValue>
{
    public override EnergyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return EnergyValue.Missing;

            case JsonTokenType.Number:
                return EnergyValue.Scalar(reader.GetDouble());

            case JsonTokenType.String:
                return TryParse(reader.GetString(), out var parsed)
                    ? EnergyValue.Scalar(parsed)
                    : EnergyValue.Missing;

            case JsonTokenType.StartArray:
                var values = new List<double>(2);

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.Number:
                            values.Add(reader.GetDouble());
                            break;
                        case JsonTokenType.String when TryParse(reader.GetString(), out var item):
                            values.Add(item);
                            break;
                        case JsonTokenType.Null:
                            // Un canal sin sensor: se guarda como cero para no correr los indices.
                            values.Add(0d);
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                return values.Count > 0 ? EnergyValue.Array([.. values]) : EnergyValue.Missing;

            default:
                reader.Skip();
                return EnergyValue.Missing;
        }
    }

    public override void Write(Utf8JsonWriter writer, EnergyValue value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        if (!value.IsArray)
        {
            writer.WriteNumberValue(value.Channel(0)!.Value);
            return;
        }

        writer.WriteStartArray();
        for (var i = 0; i < value.ChannelCount; i++)
            writer.WriteNumberValue(value.Channel(i)!.Value);
        writer.WriteEndArray();
    }

    private static bool TryParse(string? text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
