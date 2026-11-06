using System.Globalization;
using EcoWattCasa.Application.Server;
using Microsoft.Extensions.Logging;

namespace EcoWattCasa.Infrastructure.Server;

/// <summary>
/// Lee /proc y el disco de la raiz. Desde un contenedor Docker sin lxcfs, /proc/stat,
/// /proc/meminfo, /proc/loadavg y /proc/uptime son los del host, y la raiz del contenedor es un
/// overlay sobre el disco del host: no hace falta montar nada para ver el servidor entero.
///
/// El uso de CPU necesita dos muestras. Se guarda la del pedido anterior: con la pantalla
/// pidiendo cada 5 s, el porcentaje es el promedio de esos 5 s, que es lo que interesa ver. Si
/// la anterior es vieja (nadie miro la pantalla en un rato) se toman dos muestras seguidas.
/// </summary>
public sealed class ProcHostMetricsReader(ILogger<ProcHostMetricsReader> logger) : IHostMetricsReader
{
    private static readonly TimeSpan MaxSampleAge = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FreshSampleGap = TimeSpan.FromMilliseconds(400);

    private readonly Lock _lock = new();
    private (CpuTimes Times, DateTimeOffset At)? _previous;

    public HostMetrics? Read()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/meminfo"))
            return null;

        try
        {
            var memory = ParseMeminfo(File.ReadAllText("/proc/meminfo"));
            var (load1, load5, load15) = ParseLoadavg(File.ReadAllText("/proc/loadavg"));
            var uptime = ParseUptime(File.ReadAllText("/proc/uptime"));
            var disk = new DriveInfo("/");

            // Como df: ext4 le reserva a root un 5 % que nadie mas puede usar. Contado como
            // ocupado, el disco del servidor daria 6 % con 12 GB de 915; el total es lo usado mas
            // lo disponible.
            var diskUsed = disk.TotalSize - disk.TotalFreeSpace;

            return new HostMetrics(
                CpuPercent(),
                Environment.ProcessorCount,
                load1,
                load5,
                load15,
                memory.TotalBytes,
                memory.AvailableBytes,
                memory.SwapTotalBytes,
                memory.SwapFreeBytes,
                diskUsed + disk.AvailableFreeSpace,
                disk.AvailableFreeSpace,
                uptime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            logger.LogWarning(ex, "No se pudo leer /proc.");
            return null;
        }
    }

    private double? CpuPercent()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var current = ParseCpuTimes(File.ReadAllText("/proc/stat"));

            if (_previous is not { } previous || now - previous.At > MaxSampleAge)
            {
                // Bloquea el pedido 400 ms, pero solo el primero despues de un rato sin mirar.
                Thread.Sleep(FreshSampleGap);
                previous = (current, now);
                now = DateTimeOffset.UtcNow;
                current = ParseCpuTimes(File.ReadAllText("/proc/stat"));
            }

            _previous = (current, now);
            return Usage(previous.Times, current);
        }
    }

    // ---------- Parseo: funciones puras, testeadas con texto real del servidor ----------

    internal readonly record struct CpuTimes(ulong Idle, ulong Total);

    internal readonly record struct MemoryInfo(long TotalBytes, long AvailableBytes, long SwapTotalBytes, long SwapFreeBytes);

    /// <summary>La linea "cpu" de /proc/stat: idle + iowait cuentan como ocioso.</summary>
    internal static CpuTimes ParseCpuTimes(string procStat)
    {
        var line = procStat.Split('\n').First(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(v => ulong.Parse(v, CultureInfo.InvariantCulture))
            .ToArray();

        // user nice system idle iowait irq softirq steal [guest guest_nice]. guest ya esta
        // contado dentro de user, asi que se suman solo los primeros ocho.
        var total = values.Take(8).Aggregate(0UL, (sum, v) => sum + v);
        var idle = values[3] + (values.Length > 4 ? values[4] : 0);
        return new CpuTimes(idle, total);
    }

    /// <returns>null si no paso tiempo entre las dos muestras.</returns>
    internal static double? Usage(CpuTimes before, CpuTimes after)
    {
        if (after.Total <= before.Total)
            return null;

        var total = after.Total - before.Total;
        var idle = after.Idle >= before.Idle ? after.Idle - before.Idle : 0;
        return Math.Round(100.0 * (total - Math.Min(idle, total)) / total, 1);
    }

    /// <summary>
    /// MemAvailable y no MemFree: el kernel usa la RAM libre como cache de disco, y MemFree
    /// daria "casi llena" en una maquina que esta bien.
    /// </summary>
    internal static MemoryInfo ParseMeminfo(string meminfo)
    {
        var kb = meminfo.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(
                p => p[0].Trim(),
                p => long.Parse(p[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture));

        long Bytes(string key) => kb.TryGetValue(key, out var v) ? v * 1024 : 0;

        return new MemoryInfo(Bytes("MemTotal"), Bytes("MemAvailable"), Bytes("SwapTotal"), Bytes("SwapFree"));
    }

    internal static (double Load1, double Load5, double Load15) ParseLoadavg(string loadavg)
    {
        var parts = loadavg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (Parse(parts[0]), Parse(parts[1]), Parse(parts[2]));
    }

    internal static TimeSpan ParseUptime(string uptime)
        => TimeSpan.FromSeconds(Parse(uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]));

    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
