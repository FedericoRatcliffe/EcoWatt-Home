using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoWattCasa.Infrastructure.Persistence.Configurations;

public sealed class EnergyReadingConfiguration : IEntityTypeConfiguration<EnergyReading>
{
    public void Configure(EntityTypeBuilder<EnergyReading> builder)
    {
        builder.ToTable("energy_readings");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityByDefaultColumn();

        builder.Property(r => r.Timestamp).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.Watts).IsRequired();
        builder.Property(r => r.Voltage).IsRequired();
        builder.Property(r => r.Amperage).IsRequired();

        builder.HasOne(r => r.Device)
            .WithMany(d => d.Readings)
            .HasForeignKey(r => r.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indice de trabajo: todas las consultas de historial y de dashboard filtran
        // por dispositivo y rango de tiempo.
        builder.HasIndex(r => new { r.DeviceId, r.Timestamp })
            .HasDatabaseName("ix_energy_readings_device_timestamp");

        // Los agregados del dashboard barren por rango de tiempo sobre todos los dispositivos.
        builder.HasIndex(r => r.Timestamp)
            .HasDatabaseName("ix_energy_readings_timestamp");
    }
}
