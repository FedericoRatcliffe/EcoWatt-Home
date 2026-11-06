using EcoWattCasa.Application.Server;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EcoWattCasa.Infrastructure.Server;

/// <summary>
/// Tamano de la base, lecturas crudas y la ultima hora consolidada, en una sola consulta.
/// La cantidad de lecturas sale de reltuples (la estimacion que mantiene autovacuum) y no de un
/// count(*): la pantalla la pide cada 5 s y el numero exacto no cambia nada.
/// </summary>
public sealed class PostgresDatabaseStatsReader(EcoWattDbContext db) : IDatabaseStatsReader
{
    private const string Sql = """
        SELECT pg_database_size(current_database()),
               (SELECT GREATEST(reltuples, 0)::bigint FROM pg_class WHERE oid = 'energy_readings'::regclass),
               (SELECT max(hour_utc) FROM energy_hourly)
        """;

    public async Task<DatabaseStats> ReadAsync(CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = Sql;

        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        return new DatabaseStats(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }
}
