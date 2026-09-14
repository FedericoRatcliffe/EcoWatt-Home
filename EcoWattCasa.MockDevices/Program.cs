using System.Buffers;
using System.Text;
using EcoWattCasa.MockDevices;
using MQTTnet;
using MQTTnet.Protocol;

// Publisher MQTT falso: hace de tres Sonoff POW R2 con Tasmota para poder desarrollar
// el sistema completo antes de tener el hardware.
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

var devices = BuildDevices();

if (options.BackfillDays > 0)
    return await Backfill.RunAsync(devices, options);

return await RunLiveAsync(devices, options);

static SimulatedDevice[] BuildDevices()
{
    // El contador acumulado arranca con historia, como un enchufe que ya venia enchufado.
    var totalStart = DateTimeOffset.UtcNow.AddMonths(-3);

    // Los perfiles estan calibrados contra las facturas reales de la casa: ~175 kWh/mes para
    // toda la casa (243 W promedio). Estos tres suman ~3,2 kWh/dia (~95 kWh/mes), algo mas de
    // la mitad del total, que es lo que se espera al medir solo algunos enchufes.
    return
    [
        // PC con monitores: reposo bajo y picos cuando compila o juega. ~1,7 kWh/dia.
        new SimulatedDevice("sonoff-pc", new RandomWalkProfile(idleWatts: 65, minWatts: 35, maxWatts: 240), totalStart),

        // Heladera: compresor 14 min prendido a 130 W, 26 min en reposo. ~1,1 kWh/dia.
        new SimulatedDevice("sonoff-heladera",
            new DutyCycleProfile(TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(26), wattsOn: 130, wattsOff: 2),
            totalStart),

        // Lavarropas: un ciclo de 45 min por dia y el resto en standby. ~0,36 kWh/dia.
        new SimulatedDevice("sonoff-lavarropas",
            new DutyCycleProfile(TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(1395), wattsOn: 450, wattsOff: 1),
            totalStart)
    ];
}

static async Task<int> RunLiveAsync(SimulatedDevice[] devices, MockOptions options)
{
    var rng = new Random(options.Seed);
    var byTopic = devices.ToDictionary(d => d.Topic, StringComparer.OrdinalIgnoreCase);

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
    Console.WriteLine($"Dispositivos: {string.Join(", ", devices.Select(d => d.Topic))}");
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
            foreach (var device in devices)
            {
                var sample = device.Step(interval, now, rng);
                await PublishAsync(client, device.TelemetryTopic, device.ToTasmotaJson(sample), cts.Token);
                Console.WriteLine($"{now:HH:mm:ss}  {device.Topic,-20} {sample.Watts,7:0.0} W  total {sample.TotalKwh,8:0.000} kWh{(device.RelayOn ? "" : "  [apagado]")}");
            }

            Console.WriteLine();
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
