using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Domain.Interfaces;

public interface IDeviceRepository
{
    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default);
    Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Device?> GetByMqttTopicAsync(string mqttTopic, CancellationToken ct = default);

    /// <summary>
    /// El medidor de tablero activo, del que sale el consumo total de la casa. null si
    /// todavia no hay ninguno instalado, y ahi el total se estima sumando los enchufes.
    /// </summary>
    Task<Device?> GetHouseMeterAsync(CancellationToken ct = default);
    Task<Device> AddAsync(Device device, CancellationToken ct = default);
    Task UpdateAsync(Device device, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
