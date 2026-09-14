using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using EcoWattCasa.Domain.Interfaces;

namespace EcoWattCasa.Application.Services;

/// <summary>Se lanza cuando el pedido es invalido por reglas de negocio (no por formato).</summary>
public sealed class DeviceValidationException(string message) : Exception(message);

public sealed class DeviceService(
    IDeviceRepository devices,
    IEnergyReadingRepository readings,
    IDeviceCommandPublisher commands)
{
    public async Task<IReadOnlyList<DeviceDto>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await devices.GetAllAsync(ct);
        var latest = await readings.GetLatestPerDeviceAsync(ct);
        var byDevice = latest.GroupBy(r => r.DeviceId).ToDictionary(g => g.Key, g => g.First());

        return all.Select(d => Map(d, byDevice.TryGetValue(d.Id, out var r) ? r : null)).ToList();
    }

    public async Task<DeviceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(id, ct);
        if (device is null)
            return null;

        var latest = await readings.GetLatestPerDeviceAsync(ct);
        return Map(device, latest.FirstOrDefault(r => r.DeviceId == id));
    }

    public async Task<DeviceDto> CreateAsync(CreateDeviceDto dto, CancellationToken ct = default)
    {
        var topic = dto.MqttTopic.Trim();
        if (await devices.GetByMqttTopicAsync(topic, ct) is not null)
            throw new DeviceValidationException($"Ya existe un dispositivo con el topic '{topic}'.");

        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            MqttTopic = topic,
            Location = dto.Location.Trim(),
            NominalWatts = dto.NominalWatts,
            Type = ParseType(dto.Type),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var saved = await devices.AddAsync(device, ct);
        return Map(saved, null);
    }

    public async Task<DeviceDto?> UpdateAsync(Guid id, UpdateDeviceDto dto, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(id, ct);
        if (device is null)
            return null;

        var topic = dto.MqttTopic.Trim();
        var owner = await devices.GetByMqttTopicAsync(topic, ct);
        if (owner is not null && owner.Id != id)
            throw new DeviceValidationException($"El topic '{topic}' ya lo usa '{owner.Name}'.");

        device.Name = dto.Name.Trim();
        device.MqttTopic = topic;
        device.Location = dto.Location.Trim();
        device.NominalWatts = dto.NominalWatts;
        device.Type = ParseType(dto.Type);
        device.IsActive = dto.IsActive;

        await devices.UpdateAsync(device, ct);
        return Map(device, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(id, ct);
        if (device is null)
            return false;

        await devices.DeleteAsync(id, ct);
        return true;
    }

    public async Task<IReadOnlyList<EnergyReadingDto>> GetReadingsAsync(
        Guid deviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct);
        if (device is null)
            return [];

        var rows = await readings.GetByDeviceAsync(deviceId, fromUtc, toUtc, maxPoints, ct);
        return rows.Select(r => MapReading(r, device.Name)).ToList();
    }

    /// <summary>Prende o apaga el rele publicando cmnd/{topic}/POWER.</summary>
    public async Task<bool> SetPowerAsync(Guid deviceId, bool on, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct);
        if (device is null)
            return false;

        await commands.SetPowerAsync(device, on, ct);
        return true;
    }

    public static DeviceType ParseType(string? value)
        => Enum.TryParse<DeviceType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new DeviceValidationException(
                $"Tipo de dispositivo invalido: '{value}'. Validos: {string.Join(", ", Enum.GetNames<DeviceType>())}.");

    public static DeviceDto Map(Device d, EnergyReading? latest)
        => new(
            d.Id,
            d.Name,
            d.MqttTopic,
            d.Location,
            d.NominalWatts,
            d.Type.ToString(),
            d.IsActive,
            d.CreatedAt,
            latest is null ? null : Math.Round(latest.Watts, 2),
            latest?.Timestamp);

    public static EnergyReadingDto MapReading(EnergyReading r, string deviceName)
        => new(
            r.DeviceId,
            deviceName,
            r.Timestamp,
            Math.Round(r.Watts, 2),
            Math.Round(r.Voltage, 2),
            Math.Round(r.Amperage, 3),
            r.TotalKwh,
            r.TodayKwh,
            r.PowerFactor);
}
