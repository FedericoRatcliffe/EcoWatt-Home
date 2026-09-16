namespace EcoWattCasa.Infrastructure.Repositories;

/// <summary>
/// El SQL de agregacion, aparte para que se lea completo.
///
/// Va a mano y no en LINQ a proposito: el bucket temporal se calcula con
/// date_trunc(... AT TIME ZONE 'UTC') de forma explicita, porque date_trunc sobre timestamptz
/// depende del TimeZone de la sesion de Postgres y eso corre los buckets sin avisar.
/// </summary>
internal static class EnergyHourlySql
{
    /// <summary>Las siete columnas de agregacion que espera EnergySamples, en orden.</summary>
    private const string RawAggregates = """
                   (array_agg(r.total_kwh ORDER BY r."timestamp" ASC)
                      FILTER (WHERE r.total_kwh IS NOT NULL))[1] AS first_total,
                   (array_agg(r.total_kwh ORDER BY r."timestamp" DESC)
                      FILTER (WHERE r.total_kwh IS NOT NULL))[1] AS last_total,
                   AVG(r.watts)       AS avg_watts,
                   MAX(r.watts)       AS max_watts,
                   COUNT(*)           AS samples,
                   MIN(r."timestamp") AS first_ts,
                   MAX(r."timestamp") AS last_ts
        """;

    /// <summary>
    /// Agregado horario del rango, combinando las dos fuentes.
    ///
    /// Las horas ya consolidadas salen de energy_hourly; las que el rollup todavia no proceso
    /// (tipicamente la hora en curso) se agregan al vuelo desde las lecturas crudas. El
    /// anti-join es contra el bucket ya agregado, no fila por fila, asi que el costo es el de
    /// un scan del rango crudo, que la retencion mantiene acotado.
    /// </summary>
    public const string SelectHourlySamples = $"""
        WITH raw AS (
            SELECT (date_trunc('hour', r."timestamp" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC') AS bucket,
                   r.device_id,
        {RawAggregates}
            FROM energy_readings r
            WHERE r."timestamp" >= @from AND r."timestamp" < @to
            GROUP BY bucket, r.device_id
        )
        SELECT h.hour_utc AS bucket, h.device_id,
               h.first_total_kwh, h.last_total_kwh,
               h.avg_watts, h.max_watts, h.sample_count,
               h.first_timestamp, h.last_timestamp
        FROM energy_hourly h
        WHERE h.hour_utc >= @from AND h.hour_utc < @to

        UNION ALL

        SELECT raw.bucket, raw.device_id,
               raw.first_total, raw.last_total,
               raw.avg_watts, raw.max_watts, raw.samples,
               raw.first_ts, raw.last_ts
        FROM raw
        WHERE NOT EXISTS (
            SELECT 1 FROM energy_hourly h
            WHERE h.device_id = raw.device_id AND h.hour_utc = raw.bucket
        )

        ORDER BY bucket
        """;

    /// <summary>
    /// Consolida las horas ya cerradas que falten. Solo toca horas cuyo fin ya paso, para no
    /// congelar una hora a medio llenar; y no pisa las que ya estan, asi es idempotente.
    /// </summary>
    public const string RollUp = $"""
        INSERT INTO energy_hourly (
            device_id, hour_utc, first_total_kwh, last_total_kwh,
            avg_watts, max_watts, sample_count, first_timestamp, last_timestamp, rolled_up_at)
        SELECT raw.device_id, raw.bucket,
               raw.first_total, raw.last_total,
               raw.avg_watts, raw.max_watts, raw.samples,
               raw.first_ts, raw.last_ts, now()
        FROM (
            SELECT (date_trunc('hour', r."timestamp" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC') AS bucket,
                   r.device_id,
        {RawAggregates}
            FROM energy_readings r
            WHERE r."timestamp" < @completeBefore
            GROUP BY bucket, r.device_id
        ) AS raw
        ON CONFLICT (device_id, hour_utc) DO NOTHING
        """;

    /// <summary>
    /// Borra lecturas crudas viejas, pero solo de horas que ya quedaron consolidadas: sin esa
    /// condicion un rollup que fallo se llevaria los datos puestos.
    /// </summary>
    public const string PruneRaw = """
        DELETE FROM energy_readings r
        WHERE r."timestamp" < @deleteBefore
          AND EXISTS (
              SELECT 1 FROM energy_hourly h
              WHERE h.device_id = r.device_id
                AND h.hour_utc = (date_trunc('hour', r."timestamp" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')
          )
        """;
}
