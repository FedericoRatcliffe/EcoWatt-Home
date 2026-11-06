using EcoWattCasa.Application.Alerts;

namespace EcoWattCasa.Application.Server;

/// <summary>Todo lo que se sabe del servidor al momento de evaluar.</summary>
/// <param name="Host">null donde no hay /proc.</param>
/// <param name="Report">null si el script del host no dejo reporte (o no hay script).</param>
/// <param name="ReportConfigured">Si se espera reporte: en desarrollo no hay script y su falta no es un problema.</param>
/// <param name="Database">null si la consulta fallo.</param>
public sealed record ServerHealthContext(
    HostMetrics? Host,
    HostReport? Report,
    bool ReportConfigured,
    DatabaseStats? Database,
    MqttStatus Mqtt,
    IReadOnlyList<DeviceStatus> Devices,
    TimeSpan SilenceAfter);

public enum ServerOverall
{
    Ok,
    Warning,
    Critical
}

/// <summary>
/// Las reglas de salud del servidor. Funcion pura del contexto, como <see cref="AlertRules"/>, y
/// con el mismo <see cref="Alert"/> de salida para que el frontend las muestre igual.
///
/// Los umbrales son fijos a proposito: es una sola maquina conocida (AMD A4-7300, 3,2 GB, disco
/// de 1 TB) y no hay nada que ganar haciendolos configurables.
/// </summary>
public static class ServerHealth
{
    /// <summary>Los <c>container_name</c> de docker-compose.yml. Si falta uno, el stack esta roto.</summary>
    public static readonly IReadOnlyList<string> ExpectedContainers = ["ecowatt-postgres", "ecowatt-api", "ecowatt-frontend"];

    /// <summary>
    /// El script corre cada minuto. Cinco sin reporte ya no es un minuto lento: es cron o el
    /// script que dejaron de andar, y los datos de contenedores y backups quedaron viejos.
    /// </summary>
    public static readonly TimeSpan ReportStaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>El backup corre a las 3:30; 26 h deja margen para una corrida que tardo.</summary>
    public static readonly TimeSpan BackupStaleAfter = TimeSpan.FromHours(26);

    /// <summary>
    /// El rollup corre cada 15 min y solo cierra horas terminadas: la ultima consolidada tiene
    /// normalmente entre 1 y 2 h. Con 3 h ya se salteo varias pasadas.
    /// </summary>
    public static readonly TimeSpan RollupStaleAfter = TimeSpan.FromHours(3);

    public static IReadOnlyList<Alert> Evaluate(ServerHealthContext context, DateTimeOffset nowUtc)
    {
        var checks = new List<Alert>();

        checks.AddRange(System(context.Host));
        checks.AddRange(Report(context, nowUtc));
        checks.AddRange(Database(context.Database, nowUtc));
        checks.AddRange(Telemetry(context, nowUtc));

        return checks.OrderByDescending(a => a.Severity).ToList();
    }

    /// <summary>Ok si no hay nada por encima de Info: un Info es un dato, no un problema.</summary>
    public static ServerOverall Overall(IReadOnlyList<Alert> checks)
    {
        if (checks.Any(c => c.Severity == AlertSeverity.Critical))
            return ServerOverall.Critical;

        return checks.Any(c => c.Severity == AlertSeverity.Warning) ? ServerOverall.Warning : ServerOverall.Ok;
    }

    // ---------- Sistema ----------

    private static IEnumerable<Alert> System(HostMetrics? host)
    {
        if (host is null)
        {
            yield return new Alert(
                "host-metrics-missing",
                AlertSeverity.Info,
                "Sin metricas del sistema",
                "La API no corre sobre Linux (o no puede leer /proc): CPU, memoria y disco solo se ven en el servidor.");

            yield break;
        }

        if (host.DiskTotalBytes > 0)
        {
            var used = 1 - (double)host.DiskFreeBytes / host.DiskTotalBytes;
            if (used >= 0.80)
            {
                yield return new Alert(
                    "disk-space",
                    used >= 0.90 ? AlertSeverity.Critical : AlertSeverity.Warning,
                    "Disco casi lleno",
                    $"Usado {used * 100:0} %. Con el disco lleno Postgres deja de escribir y se pierden lecturas.");
            }
        }

        if (host.MemoryTotalBytes > 0)
        {
            var available = (double)host.MemoryAvailableBytes / host.MemoryTotalBytes;
            if (available < 0.10)
            {
                yield return new Alert(
                    "memory-low",
                    available < 0.05 ? AlertSeverity.Critical : AlertSeverity.Warning,
                    "Poca memoria libre",
                    $"Queda {available * 100:0} % disponible. Si se acaba, el kernel mata procesos y puede caer la API o Postgres.");
            }
        }

        // El swap es zram (RAM comprimida) y despues un archivo en el disco: que se use un poco
        // es normal, pero la mitad lleno es que la RAM ya no alcanza.
        if (host.SwapTotalBytes > 0)
        {
            var swapUsed = 1 - (double)host.SwapFreeBytes / host.SwapTotalBytes;
            if (swapUsed >= 0.5)
            {
                yield return new Alert(
                    "swap-high",
                    AlertSeverity.Warning,
                    "Mucho swap en uso",
                    $"Usado {swapUsed * 100:0} %. La RAM no alcanza y el servidor se pone lento.");
            }
        }

        // Carga por nucleo en 5 min: el promedio de 1 min salta con cualquier build o backup.
        if (host.CpuCores > 0 && host.Load5 / host.CpuCores >= 1.5)
        {
            yield return new Alert(
                "load-high",
                AlertSeverity.Warning,
                "Carga alta",
                $"Carga de {host.Load5:0.00} en 5 min con {host.CpuCores} nucleos: hay procesos esperando CPU.");
        }
    }

    // ---------- Reporte del host: contenedores, disco, backups ----------

    private static IEnumerable<Alert> Report(ServerHealthContext context, DateTimeOffset nowUtc)
    {
        if (context.Report is not { } report)
        {
            yield return context.ReportConfigured
                ? new Alert(
                    "report-missing",
                    AlertSeverity.Warning,
                    "No hay reporte del servidor",
                    "El script ecowatt-status.py no dejo su archivo: no se ven los contenedores, el disco ni los backups.")
                : new Alert(
                    "report-not-configured",
                    AlertSeverity.Info,
                    "Sin reporte del host",
                    "Contenedores, disco y backups solo se ven en el servidor, donde corre el script que los junta.");

            yield break;
        }

        var age = nowUtc - report.GeneratedUtc;
        if (age > ReportStaleAfter)
        {
            yield return new Alert(
                "report-stale",
                AlertSeverity.Warning,
                "El reporte del servidor esta viejo",
                $"Es de hace {Humanize(age)}: cron o el script dejaron de correr. Lo de contenedores, disco y backups puede no ser lo de ahora.");
        }

        foreach (var alert in Containers(report))
            yield return alert;

        foreach (var alert in Disk(report.Disk))
            yield return alert;

        foreach (var alert in Backups(report.Backups, nowUtc))
            yield return alert;

        if (report.RebootRequired)
        {
            yield return new Alert(
                "reboot-required",
                AlertSeverity.Info,
                "Hay que reiniciar el servidor",
                "Una actualizacion automatica lo pide (casi siempre, el kernel). Se puede hacer cuando convenga: son un par de minutos sin lecturas.");
        }

        foreach (var error in report.Errors)
        {
            yield return new Alert(
                "report-error",
                AlertSeverity.Warning,
                "El reporte vino incompleto",
                error);
        }
    }

    private static IEnumerable<Alert> Containers(HostReport report)
    {
        var byName = report.Containers.ToDictionary(c => c.Name, StringComparer.Ordinal);

        foreach (var name in ExpectedContainers)
        {
            if (!byName.TryGetValue(name, out var container))
            {
                yield return new Alert(
                    "container-missing",
                    AlertSeverity.Critical,
                    $"Falta el contenedor {name}",
                    "No existe en el servidor. Hay que volver a levantar el stack con el deploy.");

                continue;
            }

            if (!string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase))
            {
                yield return new Alert(
                    "container-down",
                    AlertSeverity.Critical,
                    $"{name} no esta corriendo",
                    $"Estado: {container.State}.");

                continue;
            }

            if (string.Equals(container.Health, "unhealthy", StringComparison.OrdinalIgnoreCase))
            {
                yield return new Alert(
                    "container-unhealthy",
                    AlertSeverity.Critical,
                    $"{name} esta corriendo pero no responde",
                    "Su healthcheck falla.");
            }

            // Docker lo reinicia solo (restart: unless-stopped) y desde afuera no se nota nada.
            // Cada deploy recrea los contenedores y vuelve la cuenta a cero, asi que uno solo ya
            // es un crash desde el ultimo deploy.
            if (container.RestartCount > 0)
            {
                yield return new Alert(
                    "container-restarts",
                    AlertSeverity.Warning,
                    $"{name} se reinicio solo",
                    $"{Plural(container.RestartCount, "vez", "veces")} desde el ultimo deploy: se cayo y Docker lo levanto. Mirar sus logs.");
            }
        }
    }

    private static IEnumerable<Alert> Disk(DiskHealthReport? disk)
    {
        if (disk is null)
            yield break;

        if (disk.Passed == false)
        {
            yield return new Alert(
                "smart-failed",
                AlertSeverity.Critical,
                "El disco falla el autodiagnostico",
                $"SMART de {disk.Device} da FAILED: el disco se puede morir en cualquier momento. Asegurar los backups y cambiarlo.");
        }

        var bad = (disk.ReallocatedSectors ?? 0) + (disk.PendingSectors ?? 0) + (disk.UncorrectableSectors ?? 0);
        if (bad > 0)
        {
            yield return new Alert(
                "smart-sectors",
                AlertSeverity.Warning,
                "El disco tiene sectores danados",
                $"{Plural(bad, "sector", "sectores")} reasignados, pendientes o ilegibles. Si el numero crece, el disco se esta muriendo.");
        }

        if (disk.TemperatureC is >= 50)
        {
            yield return new Alert(
                "disk-hot",
                AlertSeverity.Warning,
                "Disco caliente",
                $"{disk.TemperatureC} grados. Un disco mecanico arriba de 50 se gasta mas rapido: revisar que le llegue aire.");
        }
    }

    private static IEnumerable<Alert> Backups(BackupReport? backups, DateTimeOffset nowUtc)
    {
        if (backups is null || backups.LastUtc is null)
        {
            yield return new Alert(
                "backup-missing",
                AlertSeverity.Warning,
                "No hay backups",
                "No se encontro ningun dump en ~/backups.");

            yield break;
        }

        if (backups.LastLogOk == false)
        {
            yield return new Alert(
                "backup-failed",
                AlertSeverity.Critical,
                "Fallo el ultimo backup",
                backups.LastLogLine ?? "El log no termina en ok.");
        }

        var age = nowUtc - backups.LastUtc.Value;
        if (age > BackupStaleAfter)
        {
            yield return new Alert(
                "backup-stale",
                AlertSeverity.Warning,
                "El ultimo backup es viejo",
                $"Es de hace {Humanize(age)}. El de las 3:30 no corrio.");
        }
    }

    // ---------- Base y telemetria ----------

    private static IEnumerable<Alert> Database(DatabaseStats? database, DateTimeOffset nowUtc)
    {
        if (database is null)
        {
            yield return new Alert(
                "database-unreachable",
                AlertSeverity.Critical,
                "No se pudo consultar la base",
                "La API no llega a Postgres: no se estan guardando lecturas.");

            yield break;
        }

        if (database.LastRolledUpHourUtc is { } last && nowUtc - last > RollupStaleAfter)
        {
            yield return new Alert(
                "rollup-stale",
                AlertSeverity.Warning,
                "El resumen por hora esta atrasado",
                $"La ultima hora consolidada es de hace {Humanize(nowUtc - last)}. El dashboard sigue andando con las lecturas crudas, pero mas lento.");
        }
    }

    private static IEnumerable<Alert> Telemetry(ServerHealthContext context, DateTimeOffset nowUtc)
    {
        if (!context.Mqtt.ListenerConnected)
        {
            yield return new Alert(
                "mqtt-disconnected",
                AlertSeverity.Critical,
                "La API no esta conectada al broker MQTT",
                "No entra telemetria de ningun equipo.");
        }

        var active = context.Devices.Where(d => d.IsActive).ToList();
        var silent = active
            .Where(d => d.LastSeenUtc is null || nowUtc - d.LastSeenUtc.Value > context.SilenceAfter)
            .ToList();

        if (silent.Count == 0)
            yield break;

        // El detalle de cada equipo mudo ya lo dan las alertas del dashboard; aca alcanza con
        // saber que el servidor no esta recibiendo todo.
        yield return new Alert(
            "devices-silent",
            silent.Count == active.Count ? AlertSeverity.Critical : AlertSeverity.Warning,
            silent.Count == active.Count ? "No reporta ningun equipo" : $"{Plural(silent.Count, "equipo", "equipos")} sin reportar",
            string.Join(", ", silent.Select(d => d.Name)) + $": sin lecturas hace mas de {Humanize(context.SilenceAfter)}.");
    }

    private static string Plural(long count, string singular, string plural)
        => $"{count} {(count == 1 ? singular : plural)}";

    private static string Humanize(TimeSpan span)
    {
        if (span.TotalMinutes < 60)
            return Plural((long)Math.Max(1, Math.Round(span.TotalMinutes)), "minuto", "minutos");

        if (span.TotalHours < 48)
            return Plural((long)Math.Round(span.TotalHours), "hora", "horas");

        return Plural((long)Math.Round(span.TotalDays), "dia", "dias");
    }
}
