namespace EcoWattCasa.Domain.Entities;

/// <summary>De donde salio el comando.</summary>
public enum RelayCommandSource
{
    /// <summary>Un clic en el dashboard.</summary>
    Dashboard = 0,

    /// <summary>Una automatizacion (horarios, limites). Todavia no hay ninguna.</summary>
    Automation = 1,

    /// <summary>El propio sistema (por ejemplo, al pedir el estado inicial del rele).</summary>
    System = 2
}

/// <summary>Que paso con el comando.</summary>
public enum RelayCommandOutcome
{
    /// <summary>Se publico al broker.</summary>
    Sent = 0,

    /// <summary>Rechazado: el dispositivo tiene el rele bloqueado.</summary>
    BlockedLocked = 1,

    /// <summary>Rechazado: no paso el tiempo minimo desde la ultima conmutacion.</summary>
    BlockedTooSoon = 2,

    /// <summary>Rechazado: el dispositivo no tiene rele.</summary>
    BlockedNoRelay = 3,

    /// <summary>Se intento publicar y fallo (broker caido, por ejemplo).</summary>
    Failed = 4
}

/// <summary>
/// Un intento de conmutar un rele, ejecutado o rechazado.
///
/// Se registra siempre, incluso lo que no se ejecuta: sin esto, un ON/OFF publica al broker y
/// no deja rastro de quien lo pidio ni de por que no salio. Ademas es el dato que necesita
/// cualquier automatizacion futura para poder auditarse.
/// </summary>
public class RelayCommand
{
    public long Id { get; set; }

    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>true = se pidio prender; false = apagar.</summary>
    public bool RequestedOn { get; set; }

    public RelayCommandSource Source { get; set; }
    public RelayCommandOutcome Outcome { get; set; }

    /// <summary>Explicacion en texto cuando fue rechazado o fallo.</summary>
    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Un comando que efectivamente llego al broker.</summary>
    public bool WasSent => Outcome == RelayCommandOutcome.Sent;
}
