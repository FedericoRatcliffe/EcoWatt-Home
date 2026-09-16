using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoWattCasa.Infrastructure.Persistence.Configurations;

public sealed class EnergyHourlyConfiguration : IEntityTypeConfiguration<EnergyHourly>
{
    public void Configure(EntityTypeBuilder<EnergyHourly> builder)
    {
        builder.ToTable("energy_hourly");

        // Una fila por dispositivo y hora: la clave compuesta es tambien el indice de lectura.
        builder.HasKey(h => new { h.DeviceId, h.HourUtc });

        builder.Property(h => h.HourUtc).HasColumnType("timestamptz").IsRequired();
        builder.Property(h => h.FirstTimestamp).HasColumnType("timestamptz").IsRequired();
        builder.Property(h => h.LastTimestamp).HasColumnType("timestamptz").IsRequired();
        builder.Property(h => h.RolledUpAt).HasColumnType("timestamptz").IsRequired();

        builder.HasOne(h => h.Device)
            .WithMany()
            .HasForeignKey(h => h.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Los barridos por rango de tiempo sobre todos los dispositivos (dashboard mensual).
        builder.HasIndex(h => h.HourUtc).HasDatabaseName("ix_energy_hourly_hour");
    }
}
