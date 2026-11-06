using EcoWattCasa.Application.Server;
using EcoWattCasa.Infrastructure.Mqtt;

namespace EcoWattCasa.Infrastructure.Server;

/// <summary>
/// Si la API recibe telemetria, y quien esta conectado al broker. La lista de clientes solo
/// existe con el broker embebido: con un Mosquitto externo la API es un cliente mas y no la ve.
/// </summary>
public sealed class MqttStatusReader(MqttConnection connection, EmbeddedMqttBroker? broker = null) : IMqttStatusReader
{
    public async Task<MqttStatus> ReadAsync(CancellationToken ct = default)
    {
        var clients = broker is null ? null : await broker.GetClientIdsAsync();
        return new MqttStatus(connection.IsConnected, clients);
    }
}
