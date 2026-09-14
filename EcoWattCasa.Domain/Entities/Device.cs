using EcoWattCasa.Domain.Enums;

namespace EcoWattCasa.Domain.Entities;

/// <summary>
/// Dispositivo fisico que mide consumo. El <see cref="MqttTopic"/> es el nombre de topic
/// configurado en Tasmota (ej. "sonoff-pc"), sin los prefijos tele/ ni cmnd/.
/// </summary>
public class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MqttTopic { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int NominalWatts { get; set; }
    public DeviceType Type { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<EnergyReading> Readings { get; set; } = new List<EnergyReading>();

    /// <summary>Topic de telemetria que publica Tasmota cada TelePeriod.</summary>
    public string TelemetryTopic => $"tele/{MqttTopic}/SENSOR";

    /// <summary>Topic de comando para prender/apagar el rele.</summary>
    public string PowerCommandTopic => $"cmnd/{MqttTopic}/POWER";

    /// <summary>
    /// El POW R2 lleva un contador de energia propio; el ESP32 con SCT-013 no,
    /// asi que para ese el costo se integra a partir de la potencia instantanea.
    /// </summary>
    public bool ReportsCumulativeEnergy => Type is DeviceType.SonoffPowR2 or DeviceType.Simulated;
}
