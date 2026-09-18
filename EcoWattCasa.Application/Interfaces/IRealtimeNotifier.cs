using EcoWattCasa.Application.DTOs;

namespace EcoWattCasa.Application.Interfaces;

/// <summary>Empuja lecturas nuevas al frontend. Lo implementa SignalR en la capa API.</summary>
public interface IRealtimeNotifier
{
    Task ReadingReceivedAsync(EnergyReadingDto reading, CancellationToken ct = default);

    /// <summary>Avisa que aparecio un dispositivo nuevo (auto-registrado al llegar su primer mensaje).</summary>
    Task DeviceRegisteredAsync(DeviceDto device, CancellationToken ct = default);

    /// <summary>
    /// El rele de un dispositivo cambio de estado, segun lo confirmo el propio equipo. Incluye
    /// los cambios hechos con el boton fisico del enchufe, que el backend no pidio.
    /// </summary>
    Task RelayStateChangedAsync(RelayStateDto state, CancellationToken ct = default);
}
