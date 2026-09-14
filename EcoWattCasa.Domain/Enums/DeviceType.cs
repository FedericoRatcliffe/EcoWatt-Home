namespace EcoWattCasa.Domain.Enums;

/// <summary>Tipo de hardware que reporta las mediciones.</summary>
public enum DeviceType
{
    /// <summary>Enchufe Sonoff POW R2 con firmware Tasmota. Reporta kWh acumulado.</summary>
    SonoffPowR2 = 0,

    /// <summary>ESP32 + SCT-013 en el tablero. Solo potencia instantanea, sin acumulado.</summary>
    Esp32Sct013 = 1,

    /// <summary>Dispositivo simulado por el publisher mock (desarrollo sin hardware).</summary>
    Simulated = 2
}
