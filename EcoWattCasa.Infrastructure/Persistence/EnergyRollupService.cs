using EcoWattCasa.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EcoWattCasa.Infrastructure.Persistence;

public sealed class RollupOptions
{
    public const string SectionName = "Rollup";

    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuanto consolidar. No hace falta seguido: solo cierra horas ya pasadas.</summary>
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Dias de lecturas crudas que se conservan. Tiene que cubrir la vista de detalle mas
    /// larga que consulte lecturas sin agregar (hoy, 7 dias) con margen.
    /// </summary>
    public int RawRetentionDays { get; set; } = 21;
}

/// <summary>
/// Consolida las lecturas crudas en horas y borra las crudas viejas.
///
/// Con los cinco equipos reportando cada 30 s (el TelePeriod recomendado) entran 14.400 filas
/// por dia: sin esto, en un ano la tabla llega a ~5,3 M de filas y consultar un mes obliga a
/// escanear cientos de miles cada vez que el dashboard refresca. Consolidado, un mes son
/// ~3.700 filas y las crudas quedan acotadas a la ventana de retencion.
///
/// Es seguro correrlo muchas veces: consolidar no pisa horas ya hechas, y el borrado solo
/// toca lecturas cuya hora ya quedo guardada.
/// </summary>
public sealed class EnergyRollupService(
    IServiceScopeFactory scopeFactory,
    IOptions<RollupOptions> options,
    ILogger<EnergyRollupService> logger) : BackgroundService
{
    private readonly RollupOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(_options.IntervalMinutes, 1, 24 * 60));
        var retention = TimeSpan.FromDays(Math.Clamp(_options.RawRetentionDays, 1, 3650));

        logger.LogInformation(
            "Rollup horario activo: cada {Interval} min, conservando {Days} dias de lecturas crudas.",
            interval.TotalMinutes, retention.TotalDays);

        // Una primera pasada al arrancar deja la base al dia sin esperar el primer intervalo.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(retention, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Que falle una pasada no puede tumbar el servicio: se reintenta en la siguiente.
                logger.LogError(ex, "Fallo la consolidacion horaria; se reintenta en {Interval} min.",
                    interval.TotalMinutes);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(TimeSpan retention, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var readings = scope.ServiceProvider.GetRequiredService<IEnergyReadingRepository>();

        var now = DateTimeOffset.UtcNow;

        // Solo horas ya cerradas: la hora en curso sigue recibiendo lecturas y se agrega al
        // vuelo desde las crudas hasta que termine.
        var completeBefore = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var deleteRawBefore = now - retention;

        var (hours, deleted) = await readings.RollUpAndPruneAsync(completeBefore, deleteRawBefore, ct);

        if (hours > 0 || deleted > 0)
        {
            logger.LogInformation(
                "Rollup: {Hours} horas consolidadas, {Deleted} lecturas crudas borradas (anteriores a {Cutoff:yyyy-MM-dd}).",
                hours, deleted, deleteRawBefore);
        }
    }
}
