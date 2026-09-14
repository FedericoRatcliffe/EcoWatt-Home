using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoWattCasa.Infrastructure.Persistence.Configurations;

public sealed class TariffScheduleConfiguration : IEntityTypeConfiguration<TariffSchedule>
{
    public void Configure(EntityTypeBuilder<TariffSchedule> builder)
    {
        builder.ToTable("tariff_schedules");

        builder.HasKey(t => t.Id);

        // numeric: los precios en ARS no pueden sufrir redondeo binario.
        builder.Property(t => t.FixedChargePerDay).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(t => t.ValidFrom).HasColumnType("date").IsRequired();
        builder.Property(t => t.Source).HasMaxLength(120).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz").IsRequired();

        // Una sola tarifa por fecha de vigencia: volver a importar la misma factura la reemplaza.
        builder.HasIndex(t => t.ValidFrom).IsUnique();

        builder.Ignore(t => t.TotalSurchargeRate);

        builder.HasMany(t => t.Blocks)
            .WithOne(b => b.TariffSchedule)
            .HasForeignKey(b => b.TariffScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Surcharges)
            .WithOne(s => s.TariffSchedule)
            .HasForeignKey(s => s.TariffScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.PeriodCharges)
            .WithOne(c => c.TariffSchedule)
            .HasForeignKey(c => c.TariffScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TariffBlockConfiguration : IEntityTypeConfiguration<TariffBlock>
{
    public void Configure(EntityTypeBuilder<TariffBlock> builder)
    {
        builder.ToTable("tariff_blocks");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.PricePerKwh).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(b => b.Label).HasMaxLength(80).IsRequired();
        builder.Property(b => b.Order).IsRequired();

        builder.HasIndex(b => new { b.TariffScheduleId, b.Order }).IsUnique();
    }
}

public sealed class TariffSurchargeConfiguration : IEntityTypeConfiguration<TariffSurcharge>
{
    public void Configure(EntityTypeBuilder<TariffSurcharge> builder)
    {
        builder.ToTable("tariff_surcharges");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(80).IsRequired();
        builder.Property(s => s.Rate).HasColumnType("numeric(8,5)").IsRequired();
    }
}

public sealed class TariffPeriodChargeConfiguration : IEntityTypeConfiguration<TariffPeriodCharge>
{
    public void Configure(EntityTypeBuilder<TariffPeriodCharge> builder)
    {
        builder.ToTable("tariff_period_charges");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.Amount).HasColumnType("numeric(18,4)").IsRequired();
    }
}

public sealed class ImportedBillConfiguration : IEntityTypeConfiguration<ImportedBill>
{
    public void Configure(EntityTypeBuilder<ImportedBill> builder)
    {
        builder.ToTable("imported_bills");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.InvoiceNumber).HasMaxLength(40).IsRequired();
        builder.Property(b => b.Period).HasMaxLength(20).IsRequired();
        builder.Property(b => b.ReadingFrom).HasColumnType("date").IsRequired();
        builder.Property(b => b.ReadingTo).HasColumnType("date").IsRequired();
        builder.Property(b => b.BasicAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.TotalTaxes).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.Total).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.ImportedAt).HasColumnType("timestamptz").IsRequired();

        // Reimportar el mismo comprobante actualiza la fila en vez de duplicarla.
        builder.HasIndex(b => b.InvoiceNumber).IsUnique();

        builder.Ignore(b => b.AveragePricePerKwh);
    }
}
