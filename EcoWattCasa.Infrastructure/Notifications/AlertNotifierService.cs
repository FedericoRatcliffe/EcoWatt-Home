using System.Net.Http.Json;
using EcoWattCasa.Application.Alerts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EcoWattCasa.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Cada cuanto se evaluan las alertas para avisar.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Gravedad minima que se manda. Info se queda en el dashboard.</summary>
    public AlertSeverity MinimumSeverity { get; set; } = AlertSeverity.Warning;

    /// <summary>
    /// Token del bot, el que da @BotFather. No va en appsettings.json, que esta versionado:
    /// se pasa por variable de entorno (Notifications__TelegramBotToken) o user-secrets.
    /// </summary>
    public string? TelegramBotToken { get; set; }

    /// <summary>Chat al que se manda: el de una persona o el de un grupo de la casa.</summary>
    public string? TelegramChatId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TelegramBotToken) && !string.IsNullOrWhiteSpace(TelegramChatId);
}

/// <summary>
/// Manda por Telegram las alertas nuevas y avisa cuando se resuelven.
///
/// Sin esto una alerta solo se ve con el dashboard abierto: si el medidor se calla a la
/// madrugada o la casa va a cruzar de tramo, uno se entera cuando ya paso. Telegram porque es
/// una sola llamada HTTP saliente, sin abrir puertos de la casa ni depender de una app propia.
///
/// Solo se registra si hay token y chat configurados.
/// </summary>
public sealed class AlertNotifierService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> options,
    ILogger<AlertNotifierService> logger) : BackgroundService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly NotificationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(_options.IntervalMinutes, 1, 24 * 60));
        var tracker = new AlertNotificationTracker(_options.MinimumSeverity);

        logger.LogInformation("Avisos por Telegram activos: cada {Interval} min, desde {Severity}.",
            interval.TotalMinutes, _options.MinimumSeverity);

        // La primera pasada espera un intervalo: al arrancar la API los equipos todavia no
        // reportaron y todos figurarian mudos.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await RunOnceAsync(tracker, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo el envio de avisos; se reintenta en {Interval} min.", interval.TotalMinutes);
            }
        }
    }

    private async Task RunOnceAsync(AlertNotificationTracker tracker, CancellationToken ct)
    {
        IReadOnlyList<Alert> active;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            active = await scope.ServiceProvider.GetRequiredService<AlertService>().GetActiveAsync(ct);
        }

        var batch = tracker.Next(active);

        foreach (var alert in batch.Raised)
            await SendAsync(AlertNotificationTracker.Format(alert), ct);

        foreach (var alert in batch.Resolved)
            await SendAsync(AlertNotificationTracker.FormatResolved(alert), ct);
    }

    private async Task SendAsync(string text, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{_options.TelegramBotToken}/sendMessage";
        using var response = await Http.PostAsJsonAsync(url, new { chat_id = _options.TelegramChatId, text }, ct);

        // El token va en la URL: no se loguea la URL, solo el codigo.
        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Telegram rechazo el aviso: {Status}.", (int)response.StatusCode);
    }
}
