namespace EcoWattCasa.Domain.Enums;

/// <summary>
/// Modelo de hardware. Define capacidades, no semantica de medicion: para eso esta
/// <see cref="DeviceRole"/>. Todos hablan Tasmota por MQTT.
/// </summary>
public enum DeviceType
{
    /// <summary>
    /// Athom EM2 "2 CH Energy Meter": ESP32-C3 en riel DIN, 1 canal de tension y 2 de
    /// corriente. Mide potencia real y lleva contador de kWh. No tiene rele.
    /// Template Tasmota: EnergyCols 2 | SO129 1.
    /// </summary>
    AthomEm2 = 0,

    /// <summary>
    /// Athom "Tasmota ESP32-C3 AU Plug V3" (PG05V3-AU16A-TAS): enchufe con medicion y rele,
    /// ficha AU compatible con IRAM 2073. Un solo canal de energia.
    /// </summary>
    AthomPlugV3 = 1
}
