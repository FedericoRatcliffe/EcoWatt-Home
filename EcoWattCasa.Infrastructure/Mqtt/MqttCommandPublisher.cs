using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;

namespace EcoWattCasa.Infrastructure.Mqtt;

/// <summary>Manda comandos de Tasmota: cmnd/{topic}/POWER con ON, OFF o vacio para consultar.</summary>
public sealed class MqttCommandPublisher(MqttConnection connection) : IDeviceCommandPublisher
{
    public Task SetPowerAsync(Device device, bool on, CancellationToken ct = default)
        => connection.PublishAsync(device.PowerCommandTopic, on ? "ON" : "OFF", ct);

    public Task RequestPowerStateAsync(Device device, CancellationToken ct = default)
        => connection.PublishAsync(device.PowerCommandTopic, string.Empty, ct);
}
