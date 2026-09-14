using EcoWattCasa.Application.DTOs;

namespace EcoWattCasa.Application.Interfaces;

/// <summary>Empuja lecturas nuevas al frontend. Lo implementa SignalR en la capa API.</summary>
public interface IRealtimeNotifier
{
    Task ReadingReceivedAsync(EnergyReadingDto reading, CancellationToken ct = default);

    /// <summary>Avisa que aparecio un dispositivo nuevo (auto-registrado al llegar su primer mensaje).</summary>
    Task DeviceRegisteredAsync(DeviceDto device, CancellationToken ct = default);
}
