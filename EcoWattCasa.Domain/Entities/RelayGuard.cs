namespace EcoWattCasa.Domain.Entities;

/// <summary>
/// Resultado de evaluar si un comando de rele puede salir.
/// <paramref name="RejectedAs"/> es el motivo a registrar cuando no se permite; si se permite,
/// el resultado final lo determina el publicador (Sent o Failed).
/// </summary>
public sealed record RelayDecision(bool Allowed, RelayCommandOutcome RejectedAs, string? Reason)
{
    public static RelayDecision Allow() => new(true, RelayCommandOutcome.Sent, null);

    public static RelayDecision Reject(RelayCommandOutcome outcome, string reason) => new(false, outcome, reason);
}

/// <summary>
/// Decide si un comando de rele puede ejecutarse.
///
/// Es una funcion pura del estado del dispositivo y del ultimo comando que salio, sin reloj
/// propio, para poder verificar cada caso en un test. Se aplica a TODOS los origenes: el clic
/// en el dashboard tambien pasa por aca, no solo una automatizacion.
/// </summary>
public static class RelayGuard
{
    public static RelayDecision Evaluate(Device device, RelayCommand? lastSent, DateTimeOffset nowUtc)
    {
        if (!device.HasRelay)
        {
            return RelayDecision.Reject(
                RelayCommandOutcome.BlockedNoRelay,
                $"{device.Name} es un medidor y no tiene rele.");
        }

        if (device.RelayLocked)
        {
            return RelayDecision.Reject(
                RelayCommandOutcome.BlockedLocked,
                $"{device.Name} tiene el rele bloqueado a proposito. Se desbloquea desde Configuracion.");
        }

        if (device.MinRelayIntervalSeconds <= 0 || lastSent is null)
            return RelayDecision.Allow();

        var minimum = TimeSpan.FromSeconds(device.MinRelayIntervalSeconds);
        var elapsed = nowUtc - lastSent.CreatedAt;

        // Un reloj corrido o un comando con fecha futura no deberia bloquear para siempre.
        if (elapsed < TimeSpan.Zero || elapsed >= minimum)
            return RelayDecision.Allow();

        var wait = minimum - elapsed;
        return RelayDecision.Reject(
            RelayCommandOutcome.BlockedTooSoon,
            $"{device.Name} se conmuto hace {elapsed.TotalSeconds:0} s y tiene un minimo de " +
            $"{minimum.TotalSeconds:0} s entre cambios. Faltan {wait.TotalSeconds:0} s.");
    }
}
