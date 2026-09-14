using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Domain.Interfaces;

public interface IDeviceCommandPublisher
{
    /// <summary>Publica cmnd/{topic}/POWER con ON u OFF.</summary>
    Task SetPowerAsync(Device device, bool on, CancellationToken ct = default);

    /// <summary>Pide el estado actual del rele (cmnd/{topic}/POWER sin payload).</summary>
    Task RequestPowerStateAsync(Device device, CancellationToken ct = default);
}
