using EcoWattCasa.Domain.Enums;

namespace EcoWattCasa.Domain.Entities;

/// <summary>
/// Dispositivo fisico que mide consumo. El <see cref="MqttTopic"/> es el nombre de topic
/// configurado en Tasmota (ej. "plug-heladera"), sin los prefijos tele/, stat/ ni cmnd/.
/// </summary>
public class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MqttTopic { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int NominalWatts { get; set; }

    /// <summary>Modelo de hardware.</summary>
    public DeviceType Type { get; set; }

    /// <summary>Si mide el total de la casa o un equipo puntual.</summary>
    public DeviceRole Role { get; set; }

    /// <summary>
    /// Canal de energia a leer del payload. Los enchufes tienen uno solo (0). El EM2 tiene dos,
    /// y con una sola pinza CT instalada el que mide es el 0; el otro reporta cero.
    /// </summary>
    public int ChannelIndex { get; set; }

    /// <summary>
    /// Bloquea el rele: ni el dashboard ni una automatizacion futura pueden conmutarlo.
    /// Para la heladera, donde un corte por error arruina la comida.
    /// </summary>
    public bool RelayLocked { get; set; }

    /// <summary>
    /// Segundos minimos entre dos conmutaciones del rele. Protege al compresor de la heladera
    /// del ciclado corto, y aplica a cualquier origen del comando, incluido un clic repetido
    /// en el dashboard. 0 = sin limite.
    /// </summary>
    public int MinRelayIntervalSeconds { get; set; }

    /// <summary>Ultimo estado conocido del rele, segun stat/{topic}/POWER. null = se desconoce.</summary>
    public bool? RelayOn { get; set; }

    /// <summary>Cuando se recibio ese estado.</summary>
    public DateTimeOffset? RelayStateAt { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<EnergyReading> Readings { get; set; } = new List<EnergyReading>();

    /// <summary>Topic de telemetria que publica Tasmota cada TelePeriod.</summary>
    public string TelemetryTopic => $"tele/{MqttTopic}/SENSOR";

    /// <summary>Topic donde Tasmota informa el estado del rele.</summary>
    public string PowerStateTopic => $"stat/{MqttTopic}/POWER";

    /// <summary>Topic de comando para prender/apagar el rele.</summary>
    public string PowerCommandTopic => $"cmnd/{MqttTopic}/POWER";

    /// <summary>El EM2 es solo medicion; los enchufes tienen rele.</summary>
    public bool HasRelay => Type is DeviceType.AthomPlugV3;

    /// <summary>
    /// Si se le puede mandar un ON/OFF. No incluye la ventana de tiempo minimo, que depende
    /// de cuando fue el ultimo comando y se evalua aparte.
    /// </summary>
    public bool CanToggleRelay => HasRelay && !RelayLocked;

    /// <summary>
    /// Todo el hardware comprado lleva contador de energia propio (ENERGY.Total), asi que el
    /// consumo se calcula por diferencia de ese contador y no integrando la potencia.
    /// </summary>
    public bool ReportsCumulativeEnergy => true;
}
