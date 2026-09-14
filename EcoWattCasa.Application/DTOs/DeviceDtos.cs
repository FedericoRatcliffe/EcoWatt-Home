using System.ComponentModel.DataAnnotations;

namespace EcoWattCasa.Application.DTOs;

public sealed record DeviceDto(
    Guid Id,
    string Name,
    string MqttTopic,
    string Location,
    int NominalWatts,
    string Type,
    bool IsActive,
    DateTimeOffset CreatedAt,
    double? CurrentWatts,
    DateTimeOffset? LastSeenUtc);

public class CreateDeviceDto
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre de topic de Tasmota, sin prefijos. Ej: "sonoff-pc".</summary>
    [Required, StringLength(60, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9_\-]+$", ErrorMessage = "El topic solo admite letras, numeros, guion y guion bajo.")]
    public string MqttTopic { get; set; } = string.Empty;

    [StringLength(80)]
    public string Location { get; set; } = string.Empty;

    [Range(0, 30000)]
    public int NominalWatts { get; set; }

    /// <summary>SonoffPowR2 | Esp32Sct013 | Simulated</summary>
    [Required]
    public string Type { get; set; } = "SonoffPowR2";
}

public sealed class UpdateDeviceDto : CreateDeviceDto
{
    public bool IsActive { get; set; } = true;
}
