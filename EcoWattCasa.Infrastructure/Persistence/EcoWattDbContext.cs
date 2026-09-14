using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcoWattCasa.Infrastructure.Persistence;

public class EcoWattDbContext(DbContextOptions<EcoWattDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<EnergyReading> EnergyReadings => Set<EnergyReading>();

    public DbSet<TariffSchedule> TariffSchedules => Set<TariffSchedule>();
    public DbSet<TariffBlock> TariffBlocks => Set<TariffBlock>();
    public DbSet<TariffSurcharge> TariffSurcharges => Set<TariffSurcharge>();
    public DbSet<TariffPeriodCharge> TariffPeriodCharges => Set<TariffPeriodCharge>();

    public DbSet<ImportedBill> ImportedBills => Set<ImportedBill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EcoWattDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
