using System.ComponentModel.DataAnnotations;

namespace EcoWattCasa.Application.DTOs;

/// <param name="Role">HouseMeter (medidor de tablero) | Appliance (un aparato puntual).</param>
/// <param name="HasRelay">El modelo tiene rele. El EM2 es solo medicion.</param>
/// <param name="CanToggleRelay">
/// Si el boton de encendido tiene que estar habilitado. No incluye la ventana de tiempo minimo
/// entre conmutaciones, que depende de cuando fue el ultimo comando y se evalua al enviarlo.
/// </param>
/// <param name="RelayOn">Ultimo estado conocido del rele. null = todavia no se recibio ninguno.</param>
public sealed record DeviceDto(
    Guid Id,
    string Name,
    string MqttTopic,
    string Location,
    int NominalWatts,
    string Type,
    string Role,
    int ChannelIndex,
    bool IsActive,
    DateTimeOffset CreatedAt,
    double? CurrentWatts,
    DateTimeOffset? LastSeenUtc,
    bool HasRelay,
    bool RelayLocked,
    bool CanToggleRelay,
    int MinRelayIntervalSeconds,
    bool? RelayOn,
    DateTimeOffset? RelayStateAt);

public class CreateDeviceDto
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre de topic de Tasmota, sin prefijos. Ej: "plug-heladera".</summary>
    [Required, StringLength(60, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9_\-]+$", ErrorMessage = "El topic solo admite letras, numeros, guion y guion bajo.")]
    public string MqttTopic { get; set; } = string.Empty;

    [StringLength(80)]
    public string Location { get; set; } = string.Empty;

    [Range(0, 30000)]
    public int NominalWatts { get; set; }

    /// <summary>AthomEm2 | AthomPlugV3</summary>
    [Required]
    public string Type { get; set; } = "AthomPlugV3";

    /// <summary>
    /// HouseMeter | Appliance. El default es Appliance a proposito: un dispositivo marcado por
    /// error como medidor de tablero corrompe el total de la casa, mientras que uno marcado de
    /// mas como aparato solo aparece en el desglose.
    /// </summary>
    [Required]
    public string Role { get; set; } = "Appliance";

    /// <summary>
    /// Canal de energia del payload. Los enchufes tienen uno solo; el EM2 tiene dos y con una
    /// sola pinza CT el que mide es el 0.
    /// </summary>
    [Range(0, 7)]
    public int ChannelIndex { get; set; }

    /// <summary>Bloquea el rele: ni el dashboard ni una automatizacion pueden conmutarlo.</summary>
    public bool RelayLocked { get; set; }

    /// <summary>Segundos minimos entre dos conmutaciones. 0 = sin limite.</summary>
    [Range(0, 86400)]
    public int MinRelayIntervalSeconds { get; set; }
}

public class UpdateDeviceDto : CreateDeviceDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>Lo que se borro al limpiar el historial.</summary>
public sealed record HistoryPurgeDto(int RawDeleted, int HoursDeleted, string Scope);

/// <summary>Estado del rele confirmado por el propio equipo en stat/{topic}/POWER.</summary>
public sealed record RelayStateDto(Guid DeviceId, bool On, DateTimeOffset AtUtc);

/// <summary>Un intento de conmutar un rele, ejecutado o rechazado.</summary>
/// <param name="Source">Dashboard | Automation | System.</param>
/// <param name="Outcome">Sent | BlockedLocked | BlockedTooSoon | BlockedNoRelay | Failed.</param>
public sealed record RelayCommandDto(
    Guid DeviceId,
    string DeviceName,
    bool RequestedOn,
    string Source,
    string Outcome,
    bool WasSent,
    string? Reason,
    DateTimeOffset CreatedAt);
