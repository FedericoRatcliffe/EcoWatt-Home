using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace EcoWattCasa.API.Hubs;

/// <summary>
/// Hub de tiempo real. El frontend solo escucha; no hay metodos que el cliente pueda invocar.
/// </summary>
public sealed class EnergyHub : Hub
{
    public const string Route = "/hubs/energy";

    /// <summary>Nombres de los eventos que escucha el cliente.</summary>
    public const string ReadingEvent = "readingReceived";

    public const string DeviceRegisteredEvent = "deviceRegistered";
}

/// <summary>Implementacion SignalR del notificador que usa la capa de aplicacion.</summary>
public sealed class SignalRNotifier(IHubContext<EnergyHub> hub) : IRealtimeNotifier
{
    public Task ReadingReceivedAsync(EnergyReadingDto reading, CancellationToken ct = default)
        => hub.Clients.All.SendAsync(EnergyHub.ReadingEvent, reading, ct);

    public Task DeviceRegisteredAsync(DeviceDto device, CancellationToken ct = default)
        => hub.Clients.All.SendAsync(EnergyHub.DeviceRegisteredEvent, device, ct);
}
