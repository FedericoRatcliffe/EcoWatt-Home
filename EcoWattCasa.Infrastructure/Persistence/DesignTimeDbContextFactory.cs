using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EcoWattCasa.Infrastructure.Persistence;

/// <summary>
/// Permite correr "dotnet ef migrations add" sin levantar la API. La cadena sale de
/// ECOWATT_CONNECTION si esta, y si no usa la de desarrollo local.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EcoWattDbContext>
{
    public EcoWattDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ECOWATT_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ecowatt;Username=ecowatt;Password=ecowatt";

        var options = new DbContextOptionsBuilder<EcoWattDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new EcoWattDbContext(options);
    }
}
