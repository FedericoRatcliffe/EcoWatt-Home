using System.Text.Json;
using EcoWattCasa.Application.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EcoWattCasa.Infrastructure.Server;

public sealed class ServerReportOptions
{
    public const string SectionName = "Server";

    /// <summary>
    /// Donde deja el script del host su JSON. En el servidor es un directorio del host montado
    /// de solo lectura (ver deploy/docker-compose.server.yml). Vacio = no hay script, como en
    /// la PC de desarrollo.
    /// </summary>
    public string? ReportPath { get; set; }
}

/// <summary>Lee el JSON de <c>deploy/ecowatt-status.py</c>.</summary>
public sealed class JsonHostReportReader(
    IOptions<ServerReportOptions> options,
    ILogger<JsonHostReportReader> logger) : IHostReportReader
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string? _path = string.IsNullOrWhiteSpace(options.Value.ReportPath) ? null : options.Value.ReportPath;

    public bool IsConfigured => _path is not null;

    public async Task<HostReport?> ReadAsync(CancellationToken ct = default)
    {
        if (_path is null || !File.Exists(_path))
            return null;

        try
        {
            // El script escribe a un temporal y lo renombra: nunca se lee un archivo a medias.
            await using var stream = File.OpenRead(_path);
            return Normalize(await JsonSerializer.DeserializeAsync<HostReport>(stream, Json, ct));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "No se pudo leer el reporte del host en {Path}.", _path);
            return null;
        }
    }

    /// <summary>Las listas que el script omite llegan null; el resto del codigo las asume vacias.</summary>
    internal static HostReport? Normalize(HostReport? report)
        => report is null
            ? null
            : report with { Containers = report.Containers ?? [], Errors = report.Errors ?? [] };
}
