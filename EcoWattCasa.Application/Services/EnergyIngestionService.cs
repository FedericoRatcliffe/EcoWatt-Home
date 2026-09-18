using EcoWattCasa.Application.Interfaces;
using EcoWattCasa.Application.Mqtt;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using EcoWattCasa.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EcoWattCasa.Application.Services;

/// <summary>
/// Punto de entrada de la telemetria: recibe el topic y el JSON crudo de Tasmota,
/// lo persiste y lo empuja al frontend. No sabe nada de MQTT, asi que se puede
/// testear sin broker.
/// </summary>
public sealed class EnergyIngestionService(
    IDeviceRepository devices,
    IEnergyReadingRepository readings,
    IRealtimeNotifier notifier,
    ILogger<EnergyIngestionService> logger)
{
    /// <summary>
    /// Procesa un mensaje de tele/{topic}/SENSOR.
    /// </summary>
    /// <param name="mqttTopic">Nombre de topic del dispositivo, ya sin los prefijos tele/ y /SENSOR.</param>
    /// <param name="json">Payload crudo.</param>
    public async Task IngestAsync(string mqttTopic, string json, CancellationToken ct = default)
    {
        TasmotaSensorPayload? payload;
        try
        {
            payload = TasmotaSensorPayload.Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Payload ilegible en tele/{Topic}/SENSOR: {Json}", mqttTopic, Truncate(json));
            return;
        }

        // Tasmota publica en el mismo topic mensajes sin ENERGY (STATE, sensores de temperatura, etc.).
        if (payload?.Energy is null)
        {
            logger.LogDebug("Mensaje de {Topic} sin bloque ENERGY, se ignora.", mqttTopic);
            return;
        }

        var device = await devices.GetByMqttTopicAsync(mqttTopic, ct) ?? await AutoRegisterAsync(mqttTopic, payload, ct);
        if (!device.IsActive)
        {
            logger.LogDebug("Dispositivo {Topic} marcado inactivo, se descarta la lectura.", mqttTopic);
            return;
        }

        // Se resuelve el canal configurado para este dispositivo. Los enchufes publican
        // escalares y leen el canal 0; el EM2 publica arrays y, con una sola pinza CT, tambien
        // mide en el canal 0.
        var energy = payload.Energy.ForChannel(device.ChannelIndex);

        if (energy.IsEmpty)
        {
            logger.LogWarning(
                "{Device} esta configurado en el canal {Channel} pero el payload trajo {Channels} canal(es) sin datos ahi. " +
                "Revisa ChannelIndex en Configuracion.",
                device.Name, device.ChannelIndex, payload.Energy.ChannelCount);
            return;
        }

        // El timestamp lo pone el servidor: un equipo que se reinicia pierde la hora y un reloj
        // corrido rompe el orden de las series. Tasmota.Time queda solo como referencia de log.
        var reading = new EnergyReading
        {
            DeviceId = device.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Watts = energy.Power ?? 0d,
            Voltage = energy.Voltage ?? 0d,
            Amperage = energy.Current ?? 0d,
            TotalKwh = energy.Total,
            TodayKwh = energy.Today,
            PowerFactor = energy.Factor,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await readings.AddAsync(reading, ct);
        await notifier.ReadingReceivedAsync(DeviceService.MapReading(reading, device.Name), ct);

        logger.LogDebug("{Device}: {Watts} W, total {Total} kWh", device.Name, reading.Watts, reading.TotalKwh);
    }

    /// <summary>
    /// Un topic desconocido se registra solo. Asi enchufar un Sonoff nuevo lo hace aparecer
    /// en el dashboard sin tocar la base; despues se le corrige nombre y ubicacion desde Config.
    /// </summary>
    private async Task<Device> AutoRegisterAsync(string mqttTopic, TasmotaSensorPayload payload, CancellationToken ct)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = mqttTopic,
            MqttTopic = mqttTopic,
            Location = "Sin asignar",
            NominalWatts = (int)Math.Round(payload.Energy?.Power.Channel(0) ?? 0d),
            // Se registra como enchufe a proposito, aunque el payload venga de un medidor de
            // tablero: un dispositivo mal marcado como HouseMeter corrompe el total de la casa,
            // mientras que uno marcado como enchufe solo aparece de mas en el desglose. Si hace
            // falta, se corrige el rol desde Configuracion.
            Type = DeviceType.AthomPlugV3,
            Role = DeviceRole.Appliance,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var saved = await devices.AddAsync(device, ct);
        logger.LogInformation("Dispositivo nuevo auto-registrado desde MQTT: {Topic} ({Type})", mqttTopic, saved.Type);

        await notifier.DeviceRegisteredAsync(DeviceService.Map(saved, null), ct);
        return saved;
    }

    private static string Truncate(string value)
        => value.Length <= 300 ? value : value[..300] + "...";
}
