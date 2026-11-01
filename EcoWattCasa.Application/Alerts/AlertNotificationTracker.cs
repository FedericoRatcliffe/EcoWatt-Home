namespace EcoWattCasa.Application.Alerts;

/// <summary>Lo que hay que avisar en una pasada.</summary>
/// <param name="Raised">Alertas que aparecieron desde la pasada anterior.</param>
/// <param name="Resolved">Alertas ya avisadas que dejaron de estar activas.</param>
public sealed record NotificationBatch(IReadOnlyList<Alert> Raised, IReadOnlyList<Alert> Resolved)
{
    public bool IsEmpty => Raised.Count == 0 && Resolved.Count == 0;
}

/// <summary>
/// Decide que alertas mandar afuera del dashboard. Las alertas se recalculan en cada pasada y
/// no se guardan, asi que lo que hay que recordar es solo que ya se aviso: una alerta se manda
/// cuando aparece, una vez, y cuando se va se avisa que se resolvio. Si vuelve a aparecer,
/// es otro aviso.
///
/// La identidad es el codigo mas el dispositivo, no el texto: el texto de "vas a cruzar los
/// 150 kWh" cambia con cada proyeccion, y avisar cada vez que cambia un numero es spam.
///
/// Vive en memoria. Despues de reiniciar la API se vuelven a mandar las que sigan activas, una
/// vez: para una casa es preferible a persistir avisos y resolver cuales quedaron rancios.
/// </summary>
public sealed class AlertNotificationTracker(AlertSeverity minimum = AlertSeverity.Warning)
{
    private readonly Dictionary<string, Alert> _notified = [];

    public NotificationBatch Next(IReadOnlyList<Alert> active)
    {
        var current = active
            .Where(a => a.Severity >= minimum)
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.First());

        var raised = current
            .Where(kv => !_notified.ContainsKey(kv.Key))
            .Select(kv => kv.Value)
            .OrderByDescending(a => a.Severity)
            .ToList();

        var resolved = _notified
            .Where(kv => !current.ContainsKey(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        foreach (var alert in raised)
            _notified[Key(alert)] = alert;

        foreach (var alert in resolved)
            _notified.Remove(Key(alert));

        return new NotificationBatch(raised, resolved);
    }

    private static string Key(Alert alert) => $"{alert.Code}:{alert.DeviceId}";

    /// <summary>Texto del aviso. Plano y corto: se lee en la notificacion del telefono.</summary>
    public static string Format(Alert alert)
    {
        var label = alert.Severity switch
        {
            AlertSeverity.Critical => "Critico",
            AlertSeverity.Warning => "Atencion",
            _ => "Info"
        };

        return $"[{label}] {alert.Title}\n{alert.Detail}";
    }

    public static string FormatResolved(Alert alert) => $"[Resuelto] {alert.Title}";
}
