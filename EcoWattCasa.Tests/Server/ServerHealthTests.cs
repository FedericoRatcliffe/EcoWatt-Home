using EcoWattCasa.Application.Alerts;
using EcoWattCasa.Application.Server;

namespace EcoWattCasa.Tests.Server;

/// <summary>
/// Las reglas de la pantalla "Servidor". El punto de partida es el servidor real el 06/11/2026:
/// todo bien, y cada test rompe una sola cosa.
/// </summary>
public class ServerHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 16, 15, 0, TimeSpan.Zero);
    private const long GiB = 1024L * 1024 * 1024;

    private static HostMetrics Host(
        long diskFree = 857 * GiB,
        long memoryAvailable = 2_385_832 * 1024L,
        long swapFree = 5_368_728 * 1024L,
        double load5 = 0.05)
        => new(
            CpuPercent: 3.1,
            CpuCores: 2,
            Load1: 0.08,
            Load5: load5,
            Load15: 0.07,
            MemoryTotalBytes: 3_361_880 * 1024L,
            MemoryAvailableBytes: memoryAvailable,
            SwapTotalBytes: 5_370_404 * 1024L,
            SwapFreeBytes: swapFree,
            DiskTotalBytes: 915 * GiB,
            DiskFreeBytes: diskFree,
            Uptime: TimeSpan.FromHours(49));

    private static ContainerReport Container(string name, string state = "running", string? health = null, int restarts = 0)
        => new(name, state, health, Now.AddDays(-2), restarts, 0.3, 190_000_000, name);

    private static HostReport Report(
        IReadOnlyList<ContainerReport>? containers = null,
        DiskHealthReport? disk = null,
        BackupReport? backups = null,
        DateTimeOffset? generated = null,
        bool rebootRequired = false,
        IReadOnlyList<string>? errors = null)
        => new(
            generated ?? Now.AddSeconds(-30),
            "Ubuntu 26.04.1 LTS",
            "b331478",
            rebootRequired,
            containers ?? [Container("ecowatt-api"), Container("ecowatt-frontend"), Container("ecowatt-postgres", health: "healthy")],
            disk ?? new DiskHealthReport("/dev/sda", true, 32, 11_283, 0, 0, 0),
            backups ?? new BackupReport(3, "ecowatt-2026-11-06.dump", Now.AddHours(-10), 2_302_308, "2026-11-06 03:30:03 ok 2,2M", true),
            errors ?? []);

    private static readonly IReadOnlyList<DeviceStatus> Devices =
    [
        new(Guid.NewGuid(), "Medidor de tablero", true, Now.AddSeconds(-20), IsHouseMeter: true),
        new(Guid.NewGuid(), "Zapatilla Fede", true, Now.AddSeconds(-10)),
        new(Guid.NewGuid(), "Zapatilla Fede Habitacion", false, null),
    ];

    private static ServerHealthContext Context(
        HostMetrics? host = null,
        HostReport? report = null,
        bool noReport = false,
        bool reportConfigured = true,
        DatabaseStats? database = null,
        bool noDatabase = false,
        bool listenerConnected = true,
        IReadOnlyList<DeviceStatus>? devices = null)
        => new(
            host ?? Host(),
            noReport ? null : report ?? Report(),
            reportConfigured,
            noDatabase ? null : database ?? new DatabaseStats(58_000_000, 290_000, Now.AddHours(-2)),
            new MqttStatus(listenerConnected, ["ecowatt-api", "medidor-tablero", "plug-fede"]),
            devices ?? Devices,
            TimeSpan.FromMinutes(10));

    private static IReadOnlyList<Alert> Evaluate(ServerHealthContext context) => ServerHealth.Evaluate(context, Now);

    [Fact]
    public void El_servidor_real_sano_no_tiene_ningun_chequeo()
    {
        var checks = Evaluate(Context());

        Assert.Empty(checks);
        Assert.Equal(ServerOverall.Ok, ServerHealth.Overall(checks));
    }

    // ---------- Sistema ----------

    [Theory]
    [InlineData(150, AlertSeverity.Warning)]   // 84 % usado
    [InlineData(60, AlertSeverity.Critical)]   // 93 %
    public void El_disco_avisa_al_80_y_es_critico_al_90(long freeGiB, AlertSeverity expected)
    {
        var check = Assert.Single(Evaluate(Context(host: Host(diskFree: freeGiB * GiB))));

        Assert.Equal("disk-space", check.Code);
        Assert.Equal(expected, check.Severity);
    }

    [Fact]
    public void Poca_memoria_disponible_avisa_aunque_la_cache_la_llene()
    {
        // 8 % de 3,2 GB disponible: la regla mira MemAvailable, no MemFree.
        var check = Assert.Single(Evaluate(Context(host: Host(memoryAvailable: 280_000 * 1024L))));

        Assert.Equal("memory-low", check.Code);
        Assert.Equal(AlertSeverity.Warning, check.Severity);
    }

    [Fact]
    public void Medio_swap_lleno_es_que_la_RAM_no_alcanza()
    {
        var check = Assert.Single(Evaluate(Context(host: Host(swapFree: 2_000_000 * 1024L))));

        Assert.Equal("swap-high", check.Code);
    }

    [Fact]
    public void La_carga_se_mide_por_nucleo_en_cinco_minutos()
    {
        Assert.Empty(Evaluate(Context(host: Host(load5: 2.9))));   // 1,45 por nucleo

        var check = Assert.Single(Evaluate(Context(host: Host(load5: 3.0))));
        Assert.Equal("load-high", check.Code);
    }

    [Fact]
    public void Sin_proc_es_un_dato_y_no_un_problema()
    {
        var checks = Evaluate(Context() with { Host = null });

        Assert.Equal("host-metrics-missing", Assert.Single(checks).Code);
        Assert.Equal(ServerOverall.Ok, ServerHealth.Overall(checks));
    }

    // ---------- Reporte del host ----------

    [Fact]
    public void En_desarrollo_la_falta_de_reporte_no_preocupa()
    {
        var checks = Evaluate(Context(noReport: true, reportConfigured: false));

        Assert.Equal("report-not-configured", Assert.Single(checks).Code);
        Assert.Equal(ServerOverall.Ok, ServerHealth.Overall(checks));
    }

    [Fact]
    public void En_el_servidor_la_falta_de_reporte_es_un_problema()
    {
        var check = Assert.Single(Evaluate(Context(noReport: true)));

        Assert.Equal("report-missing", check.Code);
        Assert.Equal(AlertSeverity.Warning, check.Severity);
    }

    [Fact]
    public void Un_reporte_de_hace_mas_de_cinco_minutos_esta_viejo()
    {
        Assert.Empty(Evaluate(Context(report: Report(generated: Now.AddMinutes(-4)))));

        var check = Assert.Single(Evaluate(Context(report: Report(generated: Now.AddMinutes(-12)))));
        Assert.Equal("report-stale", check.Code);
        Assert.Contains("12 minutos", check.Detail);
    }

    [Fact]
    public void Un_contenedor_parado_o_que_falta_es_critico()
    {
        var checks = Evaluate(Context(report: Report(containers:
        [
            Container("ecowatt-api"),
            Container("ecowatt-postgres", state: "exited"),
        ])));

        Assert.Equal(["container-down", "container-missing"], checks.Select(c => c.Code).Order());
        Assert.All(checks, c => Assert.Equal(AlertSeverity.Critical, c.Severity));
        Assert.Contains(checks, c => c.Title.Contains("ecowatt-frontend"));
    }

    [Fact]
    public void Postgres_corriendo_pero_unhealthy_es_critico()
    {
        var check = Assert.Single(Evaluate(Context(report: Report(containers:
        [
            Container("ecowatt-api"),
            Container("ecowatt-frontend"),
            Container("ecowatt-postgres", health: "unhealthy"),
        ]))));

        Assert.Equal("container-unhealthy", check.Code);
        Assert.Equal(AlertSeverity.Critical, check.Severity);
    }

    [Fact]
    public void Un_reinicio_solo_es_un_crash_que_Docker_tapo()
    {
        var check = Assert.Single(Evaluate(Context(report: Report(containers:
        [
            Container("ecowatt-api", restarts: 2),
            Container("ecowatt-frontend"),
            Container("ecowatt-postgres", health: "healthy"),
        ]))));

        Assert.Equal("container-restarts", check.Code);
        Assert.Contains("2 veces", check.Detail);
    }

    [Fact]
    public void SMART_fallado_es_critico_y_los_sectores_malos_avisan()
    {
        var failed = Evaluate(Context(report: Report(disk: new DiskHealthReport("/dev/sda", false, 32, 11_283, 0, 0, 0))));
        Assert.Equal(AlertSeverity.Critical, Assert.Single(failed).Severity);

        var sectors = Assert.Single(Evaluate(Context(report: Report(disk: new DiskHealthReport("/dev/sda", true, 32, 11_283, 8, 1, 0)))));
        Assert.Equal("smart-sectors", sectors.Code);
        Assert.Contains("9 sectores", sectors.Detail);
    }

    [Fact]
    public void Disco_a_50_grados_avisa()
    {
        var check = Assert.Single(Evaluate(Context(report: Report(disk: new DiskHealthReport("/dev/sda", true, 51, 11_283, 0, 0, 0)))));

        Assert.Equal("disk-hot", check.Code);
    }

    [Fact]
    public void Un_backup_de_mas_de_26_horas_es_que_no_corrio_el_de_anoche()
    {
        var check = Assert.Single(Evaluate(Context(report: Report(backups:
            new BackupReport(3, "ecowatt-2026-11-04.dump", Now.AddHours(-40), 1_821_214, "2026-11-04 03:30:03 ok 1,8M", true)))));

        Assert.Equal("backup-stale", check.Code);
    }

    [Fact]
    public void Si_el_log_no_termina_en_ok_el_backup_fallo()
    {
        var check = Assert.Single(Evaluate(Context(report: Report(backups:
            new BackupReport(3, "ecowatt-2026-11-05.dump", Now.AddHours(-10), 2_003_860, "pg_dump: error: connection refused", false)))));

        Assert.Equal("backup-failed", check.Code);
        Assert.Equal(AlertSeverity.Critical, check.Severity);
        Assert.Equal("pg_dump: error: connection refused", check.Detail);
    }

    [Fact]
    public void Reinicio_pendiente_es_informativo()
    {
        var checks = Evaluate(Context(report: Report(rebootRequired: true)));

        Assert.Equal("reboot-required", Assert.Single(checks).Code);
        Assert.Equal(ServerOverall.Ok, ServerHealth.Overall(checks));
    }

    [Fact]
    public void Una_seccion_que_el_script_no_pudo_leer_se_muestra()
    {
        var check = Assert.Single(Evaluate(Context(report: Report(errors: ["No se pudo leer el SMART del disco: TimeoutExpired"]))));

        Assert.Equal("report-error", check.Code);
        Assert.Contains("SMART", check.Detail);
    }

    // ---------- Base y telemetria ----------

    [Fact]
    public void Sin_base_es_critico()
    {
        var check = Assert.Single(Evaluate(Context(noDatabase: true)));

        Assert.Equal("database-unreachable", check.Code);
        Assert.Equal(AlertSeverity.Critical, check.Severity);
    }

    [Fact]
    public void El_rollup_atrasado_mas_de_tres_horas_avisa()
    {
        var check = Assert.Single(Evaluate(Context(database: new DatabaseStats(58_000_000, 290_000, Now.AddHours(-5)))));

        Assert.Equal("rollup-stale", check.Code);
    }

    [Fact]
    public void Sin_listener_MQTT_no_entra_telemetria()
    {
        var check = Assert.Single(Evaluate(Context(listenerConnected: false)));

        Assert.Equal("mqtt-disconnected", check.Code);
        Assert.Equal(AlertSeverity.Critical, check.Severity);
    }

    [Fact]
    public void Un_equipo_mudo_avisa_y_los_desactivados_no_cuentan()
    {
        var devices = new List<DeviceStatus>(Devices) { new(Guid.NewGuid(), "Heladera", true, Now.AddMinutes(-25)) };

        var check = Assert.Single(Evaluate(Context(devices: devices)));

        Assert.Equal("devices-silent", check.Code);
        Assert.Equal(AlertSeverity.Warning, check.Severity);
        Assert.Equal("1 equipo sin reportar", check.Title);
        Assert.StartsWith("Heladera:", check.Detail);
    }

    [Fact]
    public void Si_no_reporta_ninguno_es_critico()
    {
        var devices = Devices.Select(d => d with { LastSeenUtc = Now.AddHours(-1) }).ToList();

        var check = Assert.Single(Evaluate(Context(devices: devices)));

        Assert.Equal(AlertSeverity.Critical, check.Severity);
        Assert.Equal("No reporta ningun equipo", check.Title);
    }

    [Fact]
    public void Lo_mas_grave_va_primero_y_define_el_estado()
    {
        var checks = Evaluate(Context(
            host: Host(diskFree: 150 * GiB),
            report: Report(rebootRequired: true),
            listenerConnected: false));

        Assert.Equal(AlertSeverity.Critical, checks[0].Severity);
        Assert.Equal(AlertSeverity.Info, checks[^1].Severity);
        Assert.Equal(ServerOverall.Critical, ServerHealth.Overall(checks));
    }
}
