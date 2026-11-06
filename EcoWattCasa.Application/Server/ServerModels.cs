namespace EcoWattCasa.Application.Server;

/// <summary>
/// La maquina vista desde adentro del contenedor de la API, en el momento en que se pide. Sin
/// lxcfs, el /proc de un contenedor Docker muestra la memoria, la carga y el uptime del host,
/// asi que esto es el servidor entero y no el contenedor.
/// </summary>
/// <param name="CpuPercent">Uso de toda la maquina (100 = todos los nucleos). null si no se pudo muestrear.</param>
public sealed record HostMetrics(
    double? CpuPercent,
    int CpuCores,
    double Load1,
    double Load5,
    double Load15,
    long MemoryTotalBytes,
    long MemoryAvailableBytes,
    long SwapTotalBytes,
    long SwapFreeBytes,
    long DiskTotalBytes,
    long DiskFreeBytes,
    TimeSpan Uptime);

/// <summary>
/// Lo que la API no puede ver desde su contenedor y le cuenta un script del host
/// (<c>deploy/ecowatt-status.py</c>, cada minuto): los contenedores, el SMART del disco y los
/// backups. Ver <see cref="IHostReportReader"/> por que no se le da acceso a Docker.
/// </summary>
/// <param name="Errors">Secciones que el script no pudo leer, en texto.</param>
public sealed record HostReport(
    DateTimeOffset GeneratedUtc,
    string? OsName,
    string? DeployedCommit,
    bool RebootRequired,
    IReadOnlyList<ContainerReport> Containers,
    DiskHealthReport? Disk,
    BackupReport? Backups,
    IReadOnlyList<string> Errors);

/// <param name="State">El de Docker: running, exited, restarting...</param>
/// <param name="Health">healthy / unhealthy / starting; null si el contenedor no tiene healthcheck.</param>
/// <param name="CpuPercent">Sobre toda la maquina, igual que <see cref="HostMetrics.CpuPercent"/>.</param>
public sealed record ContainerReport(
    string Name,
    string State,
    string? Health,
    DateTimeOffset? StartedUtc,
    int RestartCount,
    double? CpuPercent,
    long? MemoryBytes,
    string? Image);

/// <param name="Passed">El autodiagnostico SMART; null si smartctl no respondio.</param>
public sealed record DiskHealthReport(
    string Device,
    bool? Passed,
    int? TemperatureC,
    long? PowerOnHours,
    long? ReallocatedSectors,
    long? PendingSectors,
    long? UncorrectableSectors);

/// <param name="LastLogLine">Ultima linea de backup.log: dice si la ultima corrida salio bien.</param>
/// <param name="LastLogOk">Si esa linea es un "ok"; null si no hay log.</param>
public sealed record BackupReport(
    int Count,
    string? LastFile,
    DateTimeOffset? LastUtc,
    long? LastSizeBytes,
    string? LastLogLine,
    bool? LastLogOk);

/// <param name="RawReadingsEstimate">Estimacion de Postgres (reltuples): contarlas cada 5 s no vale la pena.</param>
/// <param name="LastRolledUpHourUtc">La hora consolidada mas nueva; null si el rollup no corrio nunca.</param>
public sealed record DatabaseStats(
    long SizeBytes,
    long RawReadingsEstimate,
    DateTimeOffset? LastRolledUpHourUtc);

/// <param name="ListenerConnected">Si la API esta suscripta a la telemetria.</param>
/// <param name="ConnectedClients">Clientes en el broker embebido; null con un broker externo.</param>
public sealed record MqttStatus(bool ListenerConnected, IReadOnlyList<string>? ConnectedClients);
