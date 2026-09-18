using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using EcoWattCasa.Domain.Interfaces;

namespace EcoWattCasa.Application.Services;

/// <summary>Se lanza cuando el pedido es invalido por reglas de negocio (no por formato).</summary>
public sealed class DeviceValidationException(string message) : Exception(message);

/// <summary>
/// Que paso con un pedido de conmutar el rele.
///
/// "Rechazado" no es un error del pedido ni una falla del sistema: es el sistema haciendo lo
/// que se le pidio, proteger un aparato. Por eso viaja como resultado y no como excepcion, y
/// lleva el motivo en texto para poder mostrarlo tal cual.
/// </summary>
public sealed record RelayCommandResult(bool DeviceExists, bool Allowed, RelayCommandOutcome Outcome, string? Reason)
{
    public static readonly RelayCommandResult NotFound = new(false, false, RelayCommandOutcome.Failed, null);

    public static readonly RelayCommandResult Accepted = new(true, true, RelayCommandOutcome.Sent, null);

    public static RelayCommandResult Rejected(RelayCommandOutcome outcome, string reason)
        => new(true, false, outcome, reason);
}

public sealed class DeviceService(
    IDeviceRepository devices,
    IEnergyReadingRepository readings,
    IRelayCommandRepository relayCommands,
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
            Role = ParseRole(dto.Role),
            ChannelIndex = dto.ChannelIndex,
            RelayLocked = dto.RelayLocked,
            MinRelayIntervalSeconds = dto.MinRelayIntervalSeconds,
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
        device.Role = ParseRole(dto.Role);
        device.ChannelIndex = dto.ChannelIndex;
        device.RelayLocked = dto.RelayLocked;
        device.MinRelayIntervalSeconds = dto.MinRelayIntervalSeconds;
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

    /// <summary>
    /// Prende o apaga el rele publicando cmnd/{topic}/POWER, si la guarda lo permite.
    ///
    /// Todo intento queda registrado, tambien el rechazado: sin eso un ON/OFF se publica al
    /// broker sin dejar rastro de quien lo pidio ni de por que no salio.
    /// </summary>
    public async Task<RelayCommandResult> SetPowerAsync(
        Guid deviceId, bool on, RelayCommandSource source = RelayCommandSource.Dashboard, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct);
        if (device is null)
            return RelayCommandResult.NotFound;

        var lastSent = await relayCommands.GetLastSentAsync(deviceId, ct);
        var decision = RelayGuard.Evaluate(device, lastSent, DateTimeOffset.UtcNow);

        if (!decision.Allowed)
        {
            await AuditAsync(device, on, source, decision.RejectedAs, decision.Reason, ct);
            return RelayCommandResult.Rejected(decision.RejectedAs, decision.Reason!);
        }

        try
        {
            await commands.SetPowerAsync(device, on, ct);
        }
        catch (Exception ex)
        {
            // Se audita antes de propagar: un fallo del broker tambien es parte del historial.
            await AuditAsync(device, on, source, RelayCommandOutcome.Failed, ex.Message, ct);
            throw;
        }

        await AuditAsync(device, on, source, RelayCommandOutcome.Sent, null, ct);
        return RelayCommandResult.Accepted;
    }

    private Task AuditAsync(
        Device device, bool on, RelayCommandSource source, RelayCommandOutcome outcome, string? reason, CancellationToken ct)
        => relayCommands.AddAsync(
            new RelayCommand
            {
                DeviceId = device.Id,
                RequestedOn = on,
                Source = source,
                Outcome = outcome,
                Reason = reason,
                CreatedAt = DateTimeOffset.UtcNow
            },
            ct);

    /// <summary>
    /// Borra el historial de consumo sin tocar los dispositivos.
    ///
    /// Existe para el dia que llega el hardware real: la base va a tener meses de consumo
    /// simulado sobre los mismos dispositivos, y el dashboard no tiene como distinguirlo del
    /// real. Borrar el dispositivo entero no sirve, porque se llevaria el topic, el canal y el
    /// bloqueo del rele, que son justo lo que hay que conservar.
    /// </summary>
    /// <param name="deviceId">null = el historial de toda la casa.</param>
    public async Task<HistoryPurgeDto?> DeleteHistoryAsync(Guid? deviceId, CancellationToken ct = default)
    {
        var scope = "todos los dispositivos";

        if (deviceId is { } id)
        {
            var device = await devices.GetByIdAsync(id, ct);
            if (device is null)
                return null;

            scope = device.Name;
        }

        var (raw, hours) = await readings.DeleteHistoryAsync(deviceId, ct);
        return new HistoryPurgeDto(raw, hours, scope);
    }

    /// <summary>Historial de conmutaciones, incluidas las rechazadas.</summary>
    public async Task<IReadOnlyList<RelayCommandDto>> GetRelayHistoryAsync(
        Guid? deviceId, int limit = 50, CancellationToken ct = default)
    {
        var rows = await relayCommands.GetRecentAsync(deviceId, limit, ct);
        var names = (await devices.GetAllAsync(ct)).ToDictionary(d => d.Id, d => d.Name);

        return rows
            .Select(c => new RelayCommandDto(
                c.DeviceId,
                names.TryGetValue(c.DeviceId, out var name) ? name : "(borrado)",
                c.RequestedOn,
                c.Source.ToString(),
                c.Outcome.ToString(),
                c.WasSent,
                c.Reason,
                c.CreatedAt))
            .ToList();
    }

    public static DeviceType ParseType(string? value)
        => Enum.TryParse<DeviceType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new DeviceValidationException(
                $"Tipo de dispositivo invalido: '{value}'. Validos: {string.Join(", ", Enum.GetNames<DeviceType>())}.");

    public static DeviceRole ParseRole(string? value)
        => Enum.TryParse<DeviceRole>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new DeviceValidationException(
                $"Rol de dispositivo invalido: '{value}'. Validos: {string.Join(", ", Enum.GetNames<DeviceRole>())}.");

    public static DeviceDto Map(Device d, EnergyReading? latest)
        => new(
            d.Id,
            d.Name,
            d.MqttTopic,
            d.Location,
            d.NominalWatts,
            d.Type.ToString(),
            d.Role.ToString(),
            d.ChannelIndex,
            d.IsActive,
            d.CreatedAt,
            latest is null ? null : Math.Round(latest.Watts, 2),
            latest?.Timestamp,
            d.HasRelay,
            d.RelayLocked,
            d.CanToggleRelay,
            d.MinRelayIntervalSeconds,
            d.RelayOn,
            d.RelayStateAt);

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
