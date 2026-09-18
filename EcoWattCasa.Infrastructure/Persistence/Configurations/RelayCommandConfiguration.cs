using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoWattCasa.Infrastructure.Persistence.Configurations;

public sealed class RelayCommandConfiguration : IEntityTypeConfiguration<RelayCommand>
{
    public void Configure(EntityTypeBuilder<RelayCommand> builder)
    {
        builder.ToTable("relay_commands");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityByDefaultColumn();

        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(300);
        builder.Property(c => c.CreatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasOne(c => c.Device)
            .WithMany()
            .HasForeignKey(c => c.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // La consulta caliente es "ultimo comando enviado de este dispositivo", para medir la
        // ventana de tiempo minimo entre conmutaciones.
        builder.HasIndex(c => new { c.DeviceId, c.CreatedAt })
            .HasDatabaseName("ix_relay_commands_device_created");

        builder.Ignore(c => c.WasSent);
    }
}
