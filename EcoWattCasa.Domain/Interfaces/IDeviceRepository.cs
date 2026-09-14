using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Domain.Interfaces;

public interface IDeviceRepository
{
    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default);
    Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Device?> GetByMqttTopicAsync(string mqttTopic, CancellationToken ct = default);
    Task<Device> AddAsync(Device device, CancellationToken ct = default);
    Task UpdateAsync(Device device, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
