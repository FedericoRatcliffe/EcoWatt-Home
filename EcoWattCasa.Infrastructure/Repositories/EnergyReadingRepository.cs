using System.Data.Common;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Domain.ValueObjects;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace EcoWattCasa.Infrastructure.Repositories;

/// <summary>
/// Las agregaciones van en SQL a mano y no en LINQ a proposito: el bucket temporal se calcula
/// con date_trunc(... AT TIME ZONE 'UTC'), de forma explicita, porque date_trunc sobre timestamptz
/// depende del TimeZone de la sesion de Postgres y eso corre los buckets sin avisar.
/// </summary>
public sealed class EnergyReadingRepository(EcoWattDbContext db) : IEnergyReadingRepository
{
    /// <summary>Ventana por defecto para considerar que una lectura es "el consumo actual".</summary>
    private static readonly TimeSpan CurrentReadingWindow = TimeSpan.FromHours(1);

    public async Task AddAsync(EnergyReading reading, CancellationToken ct = default)
    {
        db.EnergyReadings.Add(reading);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EnergyReading>> GetByDeviceAsync(
        Guid deviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints = 2000, CancellationToken ct = default)
    {
        var capped = Math.Clamp(maxPoints, 1, 20_000);

        // Se toman las mas recientes del rango y despues se reordena ascendente para graficar.
        var rows = await db.EnergyReadings
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.Timestamp >= fromUtc && r.Timestamp < toUtc)
            .OrderByDescending(r => r.Timestamp)
            .Take(capped)
            .ToListAsync(ct);

        rows.Reverse();
        return rows;
    }

    public async Task<IReadOnlyList<EnergyReading>> GetLatestPerDeviceAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT ON (r.device_id)
                   r.id, r.device_id, r."timestamp", r.watts, r.voltage, r.amperage,
                   r.total_kwh, r.today_kwh, r.power_factor, r.created_at
            FROM energy_readings r
            WHERE r."timestamp" >= @from
            ORDER BY r.device_id, r."timestamp" DESC
            """;

        var result = new List<EnergyReading>();
        await using var command = await CreateCommandAsync(sql, ct);
        AddTimestamp(command, "from", DateTimeOffset.UtcNow - CurrentReadingWindow);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new EnergyReading
            {
                Id = reader.GetInt64(0),
                DeviceId = reader.GetGuid(1),
                Timestamp = reader.GetFieldValue<DateTimeOffset>(2),
                Watts = reader.GetDouble(3),
                Voltage = reader.GetDouble(4),
                Amperage = reader.GetDouble(5),
                TotalKwh = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                TodayKwh = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                PowerFactor = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(9)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<BucketEnergySamples>> GetHourlySamplesAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var result = new List<BucketEnergySamples>();
        await using var command = await CreateCommandAsync(EnergyHourlySql.SelectHourlySamples, ct);
        AddTimestamp(command, "from", fromUtc);
        AddTimestamp(command, "to", toUtc);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new BucketEnergySamples(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetGuid(1),
                ReadSamples(reader, offset: 2)));
        }

        return result;
    }

    public async Task<(int HoursRolledUp, int RawDeleted)> RollUpAndPruneAsync(
        DateTimeOffset completeBefore, DateTimeOffset deleteRawBefore, CancellationToken ct = default)
    {
        await using var rollUp = await CreateCommandAsync(EnergyHourlySql.RollUp, ct);
        AddTimestamp(rollUp, "completeBefore", completeBefore);
        var hours = await rollUp.ExecuteNonQueryAsync(ct);

        // El borrado va despues y solo sobre horas ya consolidadas: si el rollup no corrio,
        // no se pierde nada.
        await using var prune = await CreateCommandAsync(EnergyHourlySql.PruneRaw, ct);
        AddTimestamp(prune, "deleteBefore", deleteRawBefore);
        var deleted = await prune.ExecuteNonQueryAsync(ct);

        return (hours, deleted);
    }

    /// <summary>Lee las 7 columnas de agregacion que arrancan en <paramref name="offset"/>.</summary>
    private static EnergySamples ReadSamples(DbDataReader reader, int offset)
        => new(
            FirstTotalKwh: reader.IsDBNull(offset) ? null : reader.GetDouble(offset),
            LastTotalKwh: reader.IsDBNull(offset + 1) ? null : reader.GetDouble(offset + 1),
            AvgWatts: reader.IsDBNull(offset + 2) ? 0d : reader.GetDouble(offset + 2),
            MaxWatts: reader.IsDBNull(offset + 3) ? 0d : reader.GetDouble(offset + 3),
            SampleCount: (int)reader.GetInt64(offset + 4),
            FirstTimestamp: reader.IsDBNull(offset + 5) ? null : reader.GetFieldValue<DateTimeOffset>(offset + 5),
            LastTimestamp: reader.IsDBNull(offset + 6) ? null : reader.GetFieldValue<DateTimeOffset>(offset + 6));

    private async Task<DbCommand> CreateCommandAsync(string sql, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void AddTimestamp(DbCommand command, string name, DateTimeOffset value)
    {
        var parameter = new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = value.ToUniversalTime() };
        command.Parameters.Add(parameter);
    }
}
