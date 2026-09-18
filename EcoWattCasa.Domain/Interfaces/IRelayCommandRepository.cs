using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Domain.Interfaces;

public interface IRelayCommandRepository
{
    Task AddAsync(RelayCommand command, CancellationToken ct = default);

    /// <summary>
    /// Ultimo comando que se llego a publicar para un dispositivo. Es contra este que se mide
    /// la ventana de tiempo minimo entre conmutaciones; los rechazados no cuentan.
    /// </summary>
    Task<RelayCommand?> GetLastSentAsync(Guid deviceId, CancellationToken ct = default);

    /// <summary>Historial reciente, para mostrarlo y para auditar.</summary>
    Task<IReadOnlyList<RelayCommand>> GetRecentAsync(Guid? deviceId, int limit = 50, CancellationToken ct = default);
}
