using System.Text.Json;
using EcoWattCasa.Application.Server;
using EcoWattCasa.Infrastructure.Server;

namespace EcoWattCasa.Tests.Server;

/// <summary>
/// Lo que la API lee del host: /proc y el JSON de ecowatt-status.py. Los textos son copias
/// literales del servidor el 06/11/2026; si el formato cambia, se rompe aca y no en la pantalla.
/// </summary>
public class HostProbeParsingTests
{
    private const string ProcStat = """
        cpu  472614 2733 221457 34696672 195340 0 2803 0 0 0
        cpu0 235642 1051 111325 17346982 98461 0 1420 0 0 0
        cpu1 236972 1681 110131 17349689 96879 0 1382 0 0 0
        intr 123456 0 0
        """;

    private const string Meminfo = """
        MemTotal:        3361880 kB
        MemFree:          412000 kB
        MemAvailable:    2385832 kB
        Buffers:          120000 kB
        SwapTotal:       5370404 kB
        SwapFree:        5368728 kB
        """;

    [Fact]
    public void La_linea_cpu_cuenta_iowait_como_ocioso_y_no_suma_guest()
    {
        var times = ProcHostMetricsReader.ParseCpuTimes(ProcStat);

        Assert.Equal(34_696_672UL + 195_340UL, times.Idle);
        Assert.Equal(472_614UL + 2_733 + 221_457 + 34_696_672 + 195_340 + 0 + 2_803 + 0, times.Total);
    }

    [Fact]
    public void El_uso_sale_de_la_diferencia_entre_dos_muestras()
    {
        var before = new ProcHostMetricsReader.CpuTimes(Idle: 1_000, Total: 2_000);
        var after = new ProcHostMetricsReader.CpuTimes(Idle: 1_150, Total: 2_200);

        // 200 ticks, 150 ociosos: 25 % ocupado.
        Assert.Equal(25.0, ProcHostMetricsReader.Usage(before, after));
        Assert.Null(ProcHostMetricsReader.Usage(after, after));
    }

    [Fact]
    public void La_memoria_disponible_es_MemAvailable_no_MemFree()
    {
        var memory = ProcHostMetricsReader.ParseMeminfo(Meminfo);

        Assert.Equal(3_361_880L * 1024, memory.TotalBytes);
        Assert.Equal(2_385_832L * 1024, memory.AvailableBytes);
        Assert.Equal(5_370_404L * 1024, memory.SwapTotalBytes);
        Assert.Equal(5_368_728L * 1024, memory.SwapFreeBytes);
    }

    [Fact]
    public void Carga_y_uptime_con_punto_decimal_sin_importar_la_cultura()
    {
        Assert.Equal((0.08, 0.05, 0.07), ProcHostMetricsReader.ParseLoadavg("0.08 0.05 0.07 2/297 723185\n"));
        Assert.Equal(TimeSpan.FromSeconds(178_387.82), ProcHostMetricsReader.ParseUptime("178387.82 346966.72\n"));
    }

    /// <summary>La salida real de ecowatt-status.py: el contrato entre el script y la API.</summary>
    private const string RealReport = """
        {
          "generatedUtc": "2026-11-06T16:15:02.922830+00:00",
          "rebootRequired": false,
          "containers": [
            { "name": "ecowatt-api", "state": "running", "health": null, "startedUtc": "2026-11-04T16:25:33.602505Z",
              "restartCount": 0, "cpuPercent": 0.3, "memoryBytes": 193986560, "image": "ecowatt-api" },
            { "name": "ecowatt-postgres", "state": "running", "health": "healthy", "startedUtc": "2026-11-04T14:42:08.831155Z",
              "restartCount": 0, "cpuPercent": 3.0, "memoryBytes": 97874083, "image": "postgres:18-alpine" }
          ],
          "errors": [],
          "osName": "Ubuntu 26.04.1 LTS",
          "deployedCommit": "b331478",
          "disk": { "device": "/dev/sda", "passed": true, "temperatureC": 32, "powerOnHours": 11283,
                    "reallocatedSectors": 0, "pendingSectors": 0, "uncorrectableSectors": 0 },
          "backups": { "count": 3, "lastFile": "ecowatt-2026-11-06.dump", "lastUtc": "2026-11-06T06:30:03.032622+00:00",
                       "lastSizeBytes": 2302308, "lastLogLine": "2026-11-06 03:30:03 ok 2,2M", "lastLogOk": true }
        }
        """;

    [Fact]
    public void El_reporte_real_del_script_se_lee_entero()
    {
        var report = JsonHostReportReader.Normalize(
            JsonSerializer.Deserialize<HostReport>(RealReport, JsonHostReportReader.Json))!;

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 16, 15, 2, 922, 830, TimeSpan.Zero), report.GeneratedUtc);
        Assert.Equal("b331478", report.DeployedCommit);
        Assert.Equal(2, report.Containers.Count);

        var postgres = report.Containers[1];
        Assert.Equal("healthy", postgres.Health);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 14, 42, 8, 831, 155, TimeSpan.Zero), postgres.StartedUtc);
        Assert.Null(report.Containers[0].Health);

        Assert.True(report.Disk!.Passed);
        Assert.Equal(32, report.Disk.TemperatureC);
        Assert.True(report.Backups!.LastLogOk);
        Assert.Equal(3, report.Backups.Count);
    }

    [Fact]
    public void Si_el_script_fallo_antes_de_los_contenedores_las_listas_no_quedan_null()
    {
        const string partial = """{ "generatedUtc": "2026-11-06T16:15:02Z", "rebootRequired": false }""";

        var report = JsonHostReportReader.Normalize(
            JsonSerializer.Deserialize<HostReport>(partial, JsonHostReportReader.Json))!;

        Assert.Empty(report.Containers);
        Assert.Empty(report.Errors);
        Assert.Null(report.Disk);
    }
}
