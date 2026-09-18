using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Interfaces;
using EcoWattCasa.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EcoWattCasa.Application.Services;

/// <summary>
/// Registra el estado del rele que informa Tasmota en stat/{topic}/POWER.
///
/// Es la unica fuente de verdad del estado: el dashboard no asume que el rele quedo como lo
/// pidio, espera la confirmacion del equipo. Asi, si alguien aprieta el boton fisico del
/// enchufe, o si el comando se perdio, la pantalla igual muestra la realidad.
///
/// No sabe nada de MQTT, igual que <see cref="EnergyIngestionService"/>: recibe el topic del
/// dispositivo y el payload en texto.
/// </summary>
public sealed class RelayStateService(
    IDeviceRepository devices,
    IRealtimeNotifier notifier,
    ILogger<RelayStateService> logger)
{
    /// <param name="mqttTopic">Topic del dispositivo, sin los prefijos stat/ ni /POWER.</param>
    /// <param name="payload">Lo que publica Tasmota: "ON" u "OFF".</param>
    public async Task ApplyAsync(string mqttTopic, string payload, CancellationToken ct = default)
    {
        var state = Parse(payload);
        if (state is null)
        {
            logger.LogDebug("Estado de rele ilegible en stat/{Topic}/POWER: '{Payload}'", mqttTopic, payload);
            return;
        }

        var device = await devices.GetByMqttTopicAsync(mqttTopic, ct);
        if (device is null)
        {
            // A diferencia de la telemetria, aca no se da de alta nada: un estado de rele no
            // trae con que completar un dispositivo, y el primer SENSOR lo va a registrar.
            logger.LogDebug("Estado de rele de un topic desconocido, se ignora: {Topic}", mqttTopic);
            return;
        }

        // Si no cambio nada no se escribe: Tasmota republica el estado en cada arranque y ante
        // cada comando, y no tiene sentido tocar la base ni despertar al frontend por eso.
        if (device.RelayOn == state)
            return;

        device.RelayOn = state;
        device.RelayStateAt = DateTimeOffset.UtcNow;
        await devices.UpdateAsync(device, ct);

        logger.LogInformation("{Device}: el rele quedo en {State}", device.Name, state.Value ? "ON" : "OFF");
        await notifier.RelayStateChangedAsync(new RelayStateDto(device.Id, state.Value, device.RelayStateAt.Value), ct);
    }

    /// <summary>
    /// Tasmota publica "ON"/"OFF", pero segun el SetOption26/StateText configurado puede mandar
    /// "1"/"0" o "true"/"false". Se aceptan las tres formas.
    /// </summary>
    internal static bool? Parse(string? payload)
        => payload?.Trim().ToUpperInvariant() switch
        {
            "ON" or "1" or "TRUE" => true,
            "OFF" or "0" or "FALSE" => false,
            _ => null
        };
}
