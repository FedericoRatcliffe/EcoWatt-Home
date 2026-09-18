using System.Buffers;
using System.Text;
using EcoWattCasa.MockDevices;
using MQTTnet;
using MQTTnet.Protocol;

// Publisher MQTT falso: hace de los cinco equipos Athom comprados (el medidor de tablero y
// los cuatro enchufes) para poder desarrollar el sistema completo antes de tener el hardware.
//
//   dotnet run --project EcoWattCasa.MockDevices
//   dotnet run --project EcoWattCasa.MockDevices -- --interval 5
//   dotnet run --project EcoWattCasa.MockDevices -- --backfill 7
//
// El modo backfill NO usa MQTT: escribe historia directo en Postgres, porque la ingesta
// le pone a cada lectura la hora del servidor y por MQTT no hay forma de simular el pasado.

var options = MockOptions.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(MockOptions.HelpText);
    return 0;
}

var fleet = Fleet.Build();

if (options.BackfillDays > 0)
    return await Backfill.RunAsync(fleet, options);

return await RunLiveAsync(fleet, options);

static async Task<int> RunLiveAsync(Fleet fleet, MockOptions options)
{
    var rng = new Random(options.Seed);
    var byTopic = fleet.All.ToDictionary(d => d.Topic, StringComparer.OrdinalIgnoreCase);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var client = new MqttClientFactory().CreateMqttClient();

    var clientOptions = new MqttClientOptionsBuilder()
        .WithTcpServer(options.Host, options.Port)
        .WithClientId($"ecowatt-mock-{Environment.ProcessId}")
        .WithCleanSession()
        .Build();

    // Los comandos del dashboard llegan aca: asi el boton ON/OFF apaga de verdad el consumo.
    client.ApplicationMessageReceivedAsync += async e =>
    {
        var parts = e.ApplicationMessage.Topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !parts[0].Equals("cmnd", StringComparison.OrdinalIgnoreCase))
            return;

        if (!byTopic.TryGetValue(parts[1], out var device))
            return;

        if (!device.HasRelay)
        {
            // El EM2 no tiene rele. El equipo real simplemente no responderia.
            Console.WriteLine($"  cmnd {device.Topic} <- ignorado: el medidor no tiene rele");
            return;
        }

        var payload = e.ApplicationMessage.Payload;
        var command = (payload.IsSingleSegment
            ? Encoding.UTF8.GetString(payload.FirstSpan)
            : Encoding.UTF8.GetString(payload.ToArray())).Trim().ToUpperInvariant();

        device.RelayOn = command switch
        {
            "ON" or "1" => true,
            "OFF" or "0" => false,
            "TOGGLE" or "2" => !device.RelayOn,
            _ => device.RelayOn // payload vacio = consulta de estado
        };

        var state = device.RelayOn ? "ON" : "OFF";
        Console.WriteLine($"  cmnd {device.Topic} <- '{command}' => rele {state}");

        // Tasmota confirma en stat/{topic}/RESULT y stat/{topic}/POWER.
        await PublishAsync(client, device.ResultTopic, $"{{\"POWER\":\"{state}\"}}", CancellationToken.None);
        await PublishAsync(client, device.PowerStateTopic, state, CancellationToken.None);
    };

    Console.WriteLine($"EcoWatt mock -> broker {options.Host}:{options.Port}, cada {options.IntervalSeconds}s");
    Console.WriteLine($"Dispositivos: {string.Join(", ", fleet.All.Select(d => d.Topic))}");
    Console.WriteLine("Ctrl+C para cortar.\n");

    var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
    var connected = false;

    while (!cts.IsCancellationRequested)
    {
        try
        {
            if (!client.IsConnected)
            {
                await client.ConnectAsync(clientOptions, cts.Token);
                await client.SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic("cmnd/+/POWER").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                        .Build(),
                    cts.Token);

                Console.WriteLine($"Conectado a {options.Host}:{options.Port}, escuchando cmnd/+/POWER");
                connected = true;
            }

            var now = DateTimeOffset.Now;
            var plugWatts = 0d;

            foreach (var (device, sample) in fleet.Step(interval, now, rng))
            {
                await PublishAsync(client, device.TelemetryTopic, device.ToTasmotaJson(sample), cts.Token);

                if (device.HasRelay)
                    plugWatts += sample.Watts;

                var totalKwh = sample.Channels.Sum(c => c.TotalKwh);
                var off = device.HasRelay && !device.RelayOn ? "  [apagado]" : "";
                Console.WriteLine(
                    $"{now:HH:mm:ss}  {device.Topic,-16} {sample.Watts,7:0.0} W  total {totalKwh,8:0.000} kWh{off}");
            }

            // Lo mismo que va a mostrar el dashboard: total del tablero menos los enchufes.
            // Si alguna vez sale negativo, hay un error en la simulacion.
            Console.WriteLine($"{"",10}{"no identificado",-16} {fleet.Meter.LastWatts - plugWatts,7:0.0} W\n");
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            if (connected)
                Console.WriteLine($"  ! {ex.Message}");
            else
                Console.WriteLine($"  ! Broker no disponible en {options.Host}:{options.Port} ({ex.Message}). Reintentando...");

            connected = false;
        }

        try
        {
            await Task.Delay(interval, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    if (client.IsConnected)
        await client.DisconnectAsync();

    client.Dispose();
    Console.WriteLine("Mock detenido.");
    return 0;
}

static Task PublishAsync(IMqttClient client, string topic, string payload, CancellationToken ct)
    => client.PublishAsync(
        new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(),
        ct);
