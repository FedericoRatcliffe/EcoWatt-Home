using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoWattCasa.Infrastructure.Persistence.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(80).IsRequired();
        builder.Property(d => d.MqttTopic).HasMaxLength(60).IsRequired();
        builder.Property(d => d.Location).HasMaxLength(80).IsRequired();
        builder.Property(d => d.NominalWatts).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(d => d.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.ChannelIndex).HasDefaultValue(0);
        builder.Property(d => d.RelayLocked).HasDefaultValue(false);
        builder.Property(d => d.MinRelayIntervalSeconds).HasDefaultValue(0);
        builder.Property(d => d.RelayStateAt).HasColumnType("timestamptz");
        builder.Property(d => d.IsActive).HasDefaultValue(true);
        builder.Property(d => d.CreatedAt).IsRequired();

        // El topic identifica al dispositivo en MQTT: dos dispositivos no pueden compartirlo.
        builder.HasIndex(d => d.MqttTopic).IsUnique();

        // El rol se consulta en cada agregacion del dashboard.
        builder.HasIndex(d => d.Role).HasDatabaseName("ix_devices_role");

        // Las propiedades derivadas viven solo en memoria.
        builder.Ignore(d => d.TelemetryTopic);
        builder.Ignore(d => d.PowerStateTopic);
        builder.Ignore(d => d.PowerCommandTopic);
        builder.Ignore(d => d.HasRelay);
        builder.Ignore(d => d.CanToggleRelay);
        builder.Ignore(d => d.ReportsCumulativeEnergy);
    }
}
