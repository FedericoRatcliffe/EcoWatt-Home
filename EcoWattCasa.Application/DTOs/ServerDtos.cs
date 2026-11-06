using EcoWattCasa.Application.Alerts;
using EcoWattCasa.Application.Server;

namespace EcoWattCasa.Application.DTOs;

/// <summary>CPU, memoria, swap y disco, ya con los porcentajes hechos.</summary>
public sealed record ServerSystemDto(
    double? CpuPercent,
    int CpuCores,
    double Load1,
    double Load5,
    double Load15,
    long MemoryTotalBytes,
    long MemoryUsedBytes,
    double MemoryUsedPercent,
    long SwapTotalBytes,
    long SwapUsedBytes,
    double? SwapUsedPercent,
    long DiskTotalBytes,
    long DiskUsedBytes,
    double DiskUsedPercent,
    long UptimeSeconds);

/// <param name="ConnectedClients">null con un broker externo, donde no se ve quien esta conectado.</param>
public sealed record ServerMqttDto(bool ListenerConnected, int? ConnectedClients, IReadOnlyList<string> ClientIds);

/// <summary>Un equipo y cuando mando su ultima lectura.</summary>
public sealed record ServerDeviceDto(Guid Id, string Name, DateTimeOffset? LastSeenUtc, bool Silent);

/// <summary>La propia API y lo que ella ve de la base y del broker.</summary>
/// <param name="DatabaseSizeBytes">null si la base no respondio.</param>
public sealed record ServerAppDto(
    long ApiUptimeSeconds,
    long ApiMemoryBytes,
    long? DatabaseSizeBytes,
    long? RawReadingsEstimate,
    DateTimeOffset? LastRolledUpHourUtc,
    ServerMqttDto Mqtt,
    IReadOnlyList<ServerDeviceDto> Devices);

/// <summary>Todo el estado del servidor de una vez, para la pantalla "Servidor".</summary>
/// <param name="System">null donde no hay /proc (desarrollo en Windows).</param>
/// <param name="Report">null si el script del host no dejo reporte.</param>
/// <param name="Checks">Lo que esta mal o conviene saber, lo mas grave primero.</param>
public sealed record ServerStatusDto(
    DateTimeOffset GeneratedUtc,
    ServerOverall Overall,
    IReadOnlyList<Alert> Checks,
    ServerSystemDto? System,
    HostReport? Report,
    ServerAppDto App);
