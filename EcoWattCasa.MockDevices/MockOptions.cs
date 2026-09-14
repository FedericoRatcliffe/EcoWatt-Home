namespace EcoWattCasa.MockDevices;

internal sealed class MockOptions
{
    public const string HelpText = """
        EcoWatt Casa - publisher MQTT simulado

          --host <host>          Broker MQTT (default: localhost, o MQTT_HOST)
          --port <puerto>        Puerto del broker (default: 1883)
          --interval <segundos>  Cada cuanto publica (default: 10)
          --seed <n>             Semilla del generador, para repetir una corrida
          --backfill <dias>      No publica: genera <dias> de historia directo en Postgres
          --connection <cs>      Cadena de conexion para el backfill (o ECOWATT_CONNECTION)
          --help
        """;

    public string Host { get; private set; } = Environment.GetEnvironmentVariable("MQTT_HOST") ?? "localhost";
    public int Port { get; private set; } = 1883;
    public int IntervalSeconds { get; private set; } = 10;
    public int Seed { get; private set; } = Environment.TickCount;
    public int BackfillDays { get; private set; }
    public bool ShowHelp { get; private set; }

    public string ConnectionString { get; private set; } =
        Environment.GetEnvironmentVariable("ECOWATT_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=ecowatt;Username=ecowatt;Password=ecowatt";

    /// <summary>Resolucion de las muestras generadas en el backfill.</summary>
    public TimeSpan BackfillStep { get; } = TimeSpan.FromMinutes(1);

    public static MockOptions Parse(string[] args)
    {
        var options = new MockOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var next = i + 1 < args.Length ? args[i + 1] : null;

            switch (args[i])
            {
                case "--host" when next is not null:
                    options.Host = next;
                    i++;
                    break;
                case "--port" when int.TryParse(next, out var port):
                    options.Port = port;
                    i++;
                    break;
                case "--interval" when int.TryParse(next, out var interval):
                    options.IntervalSeconds = Math.Clamp(interval, 1, 3600);
                    i++;
                    break;
                case "--seed" when int.TryParse(next, out var seed):
                    options.Seed = seed;
                    i++;
                    break;
                case "--backfill" when int.TryParse(next, out var days):
                    options.BackfillDays = Math.Clamp(days, 1, 400);
                    i++;
                    break;
                case "--connection" when next is not null:
                    options.ConnectionString = next;
                    i++;
                    break;
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
            }
        }

        return options;
    }
}
