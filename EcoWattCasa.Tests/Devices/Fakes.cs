using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Domain.ValueObjects;

namespace EcoWattCasa.Tests.Devices;

/// <summary>
/// Repositorio de dispositivos en memoria. Guarda las mismas instancias que le dan, para que
/// un cambio hecho por el servicio se vea despues sin tener que simular el tracking de EF.
/// </summary>
internal sealed class FakeDeviceRepository(params Device[] devices) : IDeviceRepository
{
    private readonly List<Device> _devices = [.. devices];

    public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Device>>(_devices);

    public Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_devices.FirstOrDefault(d => d.Id == id));

    public Task<Device?> GetByMqttTopicAsync(string topic, CancellationToken ct = default)
        => Task.FromResult(_devices.FirstOrDefault(d => d.MqttTopic == topic));

    public Task<Device?> GetHouseMeterAsync(CancellationToken ct = default)
        => Task.FromResult(_devices.FirstOrDefault(d => d.IsActive && d.Role == DeviceRole.HouseMeter));

    public Task<Device> AddAsync(Device device, CancellationToken ct = default)
    {
        _devices.Add(device);
        return Task.FromResult(device);
    }

    public Task UpdateAsync(Device device, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _devices.RemoveAll(d => d.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>Historial de comandos en memoria, con la misma regla que el repositorio real.</summary>
internal sealed class FakeRelayCommandRepository : IRelayCommandRepository
{
    public List<RelayCommand> Commands { get; } = [];

    public Task AddAsync(RelayCommand command, CancellationToken ct = default)
    {
        Commands.Add(command);
        return Task.CompletedTask;
    }

    /// <summary>Solo los que salieron: un rechazo no movio el rele y no extiende la ventana.</summary>
    public Task<RelayCommand?> GetLastSentAsync(Guid deviceId, CancellationToken ct = default)
        => Task.FromResult(Commands
            .Where(c => c.DeviceId == deviceId && c.Outcome == RelayCommandOutcome.Sent)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<RelayCommand>> GetRecentAsync(
        Guid? deviceId, int limit = 50, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RelayCommand>>(Commands
            .Where(c => deviceId is null || c.DeviceId == deviceId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToList());
}

/// <summary>Publisher que anota lo que se le pidio, o revienta si se lo configura para eso.</summary>
internal sealed class FakeCommandPublisher : IDeviceCommandPublisher
{
    public List<(string Topic, bool On)> Published { get; } = [];

    /// <summary>Simula el broker caido.</summary>
    public Exception? ThrowOnPublish { get; set; }

    public Task SetPowerAsync(Device device, bool on, CancellationToken ct = default)
    {
        if (ThrowOnPublish is not null)
            throw ThrowOnPublish;

        Published.Add((device.MqttTopic, on));
        return Task.CompletedTask;
    }

    public Task RequestPowerStateAsync(Device device, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Las lecturas no intervienen en la conmutacion del rele.</summary>
internal sealed class EmptyReadingRepository : IEnergyReadingRepository
{
    public Task AddAsync(EnergyReading reading, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<EnergyReading>> GetLatestPerDeviceAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EnergyReading>>([]);

    public Task<IReadOnlyList<EnergyReading>> GetByDeviceAsync(
        Guid deviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints = 2000, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EnergyReading>>([]);

    public Task<IReadOnlyList<BucketEnergySamples>> GetHourlySamplesAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BucketEnergySamples>>([]);

    /// <summary>Lo que se borro, contado, para poder afirmar sobre el alcance del pedido.</summary>
    public List<Guid?> Purged { get; } = [];

    public Task<(int RawDeleted, int HoursDeleted)> DeleteHistoryAsync(
        Guid? deviceId, CancellationToken ct = default)
    {
        Purged.Add(deviceId);
        return Task.FromResult((0, 0));
    }

    public Task<(int HoursRolledUp, int RawDeleted)> RollUpAndPruneAsync(
        DateTimeOffset completeBefore, DateTimeOffset deleteRawBefore, CancellationToken ct = default)
        => Task.FromResult((0, 0));
}
