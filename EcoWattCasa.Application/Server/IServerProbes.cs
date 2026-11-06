namespace EcoWattCasa.Application.Server;

/// <summary>CPU, memoria, carga y disco de la maquina, en vivo.</summary>
public interface IHostMetricsReader
{
    /// <summary>null donde no hay /proc (la PC de desarrollo con Windows).</summary>
    HostMetrics? Read();
}

/// <summary>
/// El reporte que deja el script del host. Existe para no montarle el socket de Docker a la
/// API: con ese socket, quien encuentre una falla en una API sin autenticacion es root en el
/// servidor. Leyendo un archivo, lo peor que puede pasar es leer un archivo.
/// </summary>
public interface IHostReportReader
{
    /// <summary>false en desarrollo, donde no hay script del host.</summary>
    bool IsConfigured { get; }

    /// <summary>null si esta configurado pero el archivo no existe o no se puede leer.</summary>
    Task<HostReport?> ReadAsync(CancellationToken ct = default);
}

public interface IDatabaseStatsReader
{
    Task<DatabaseStats> ReadAsync(CancellationToken ct = default);
}

public interface IMqttStatusReader
{
    Task<MqttStatus> ReadAsync(CancellationToken ct = default);
}
