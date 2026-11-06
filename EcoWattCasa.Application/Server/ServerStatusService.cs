using System.Diagnostics;
using EcoWattCasa.Application.Alerts;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EcoWattCasa.Application.Server;

/// <summary>
/// Junta las cuatro fuentes del estado del servidor (/proc, el reporte del host, la base y el
/// broker) y se las pasa a <see cref="ServerHealth"/>.
///
/// Ninguna fuente puede tumbar la pantalla: si una falla, esa parte sale vacia y la falla se
/// convierte en un chequeo. Una pagina de estado que da 500 justo cuando algo anda mal no sirve.
/// </summary>
public sealed class ServerStatusService(
    IHostMetricsReader hostMetrics,
    IHostReportReader hostReport,
    IDatabaseStatsReader databaseStats,
    IMqttStatusReader mqttStatus,
    IDeviceRepository devices,
    IEnergyReadingRepository readings,
    IOptions<AlertThresholds> thresholds,
    ILogger<ServerStatusService> logger)
{
    public async Task<ServerStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var host = hostMetrics.Read();
        var report = await hostReport.ReadAsync(ct);
        var database = await TryAsync(() => databaseStats.ReadAsync(ct), "la base");
        var mqtt = await mqttStatus.ReadAsync(ct);
        var deviceStatuses = await TryAsync(() => DeviceStatusesAsync(ct), "los equipos") ?? [];

        var silenceAfter = TimeSpan.FromMinutes(Math.Max(1, thresholds.Value.SilenceMinutes));
        var context = new ServerHealthContext(
            host, report, hostReport.IsConfigured, database, mqtt, deviceStatuses, silenceAfter);

        var checks = ServerHealth.Evaluate(context, now);

        using var process = Process.GetCurrentProcess();
        var app = new ServerAppDto(
            ApiUptimeSeconds: (long)(now - process.StartTime.ToUniversalTime()).TotalSeconds,
            ApiMemoryBytes: process.WorkingSet64,
            DatabaseSizeBytes: database?.SizeBytes,
            RawReadingsEstimate: database?.RawReadingsEstimate,
            LastRolledUpHourUtc: database?.LastRolledUpHourUtc,
            Mqtt: new ServerMqttDto(
                mqtt.ListenerConnected,
                mqtt.ConnectedClients?.Count,
                mqtt.ConnectedClients ?? []),
            Devices: deviceStatuses
                .Where(d => d.IsActive)
                .Select(d => new ServerDeviceDto(
                    d.Id,
                    d.Name,
                    d.LastSeenUtc,
                    d.LastSeenUtc is null || now - d.LastSeenUtc.Value > silenceAfter))
                .ToList());

        return new ServerStatusDto(now, ServerHealth.Overall(checks), checks, ToDto(host), report, app);
    }

    private async Task<IReadOnlyList<DeviceStatus>> DeviceStatusesAsync(CancellationToken ct)
    {
        var all = await devices.GetAllAsync(ct);
        var lastSeen = (await readings.GetLatestPerDeviceAsync(ct))
            .GroupBy(r => r.DeviceId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Timestamp));

        return all
            .Select(d => new DeviceStatus(d.Id, d.Name, d.IsActive, lastSeen.TryGetValue(d.Id, out var seen) ? seen : null))
            .ToList();
    }

    private async Task<T?> TryAsync<T>(Func<Task<T>> read, string what) where T : class
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Estado del servidor: no se pudo leer {What}.", what);
            return null;
        }
    }

    private static ServerSystemDto? ToDto(HostMetrics? host)
    {
        if (host is null)
            return null;

        var memoryUsed = host.MemoryTotalBytes - host.MemoryAvailableBytes;
        var swapUsed = host.SwapTotalBytes - host.SwapFreeBytes;
        var diskUsed = host.DiskTotalBytes - host.DiskFreeBytes;

        return new ServerSystemDto(
            host.CpuPercent,
            host.CpuCores,
            host.Load1,
            host.Load5,
            host.Load15,
            host.MemoryTotalBytes,
            memoryUsed,
            Percent(memoryUsed, host.MemoryTotalBytes) ?? 0,
            host.SwapTotalBytes,
            swapUsed,
            Percent(swapUsed, host.SwapTotalBytes),
            host.DiskTotalBytes,
            diskUsed,
            Percent(diskUsed, host.DiskTotalBytes) ?? 0,
            (long)host.Uptime.TotalSeconds);
    }

    private static double? Percent(long part, long total)
        => total > 0 ? Math.Round(100.0 * part / total, 1) : null;
}
