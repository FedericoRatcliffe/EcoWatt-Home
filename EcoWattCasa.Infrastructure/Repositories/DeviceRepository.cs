using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoWattCasa.Infrastructure.Repositories;

public sealed class DeviceRepository(EcoWattDbContext db) : IDeviceRepository
{
    public async Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default)
        => await db.Devices
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

    public async Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Device?> GetByMqttTopicAsync(string mqttTopic, CancellationToken ct = default)
        => await db.Devices.FirstOrDefaultAsync(d => d.MqttTopic == mqttTopic, ct);

    public async Task<Device> AddAsync(Device device, CancellationToken ct = default)
    {
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);
        return device;
    }

    public async Task UpdateAsync(Device device, CancellationToken ct = default)
    {
        db.Devices.Update(device);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Las lecturas caen por el ON DELETE CASCADE de la FK.
        await db.Devices.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
    }
}
