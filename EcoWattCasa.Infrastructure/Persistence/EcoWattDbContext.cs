using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcoWattCasa.Infrastructure.Persistence;

public class EcoWattDbContext(DbContextOptions<EcoWattDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<EnergyReading> EnergyReadings => Set<EnergyReading>();

    /// <summary>Horas consolidadas: lo que consultan los graficos mas alla de la retencion.</summary>
    public DbSet<EnergyHourly> EnergyHourly => Set<EnergyHourly>();

    public DbSet<TariffSchedule> TariffSchedules => Set<TariffSchedule>();
    public DbSet<TariffBlock> TariffBlocks => Set<TariffBlock>();
    public DbSet<TariffSurcharge> TariffSurcharges => Set<TariffSurcharge>();
    public DbSet<TariffPeriodCharge> TariffPeriodCharges => Set<TariffPeriodCharge>();

    public DbSet<ImportedBill> ImportedBills => Set<ImportedBill>();

    /// <summary>Auditoria de los comandos de rele, ejecutados y rechazados.</summary>
    public DbSet<RelayCommand> RelayCommands => Set<RelayCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EcoWattDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
