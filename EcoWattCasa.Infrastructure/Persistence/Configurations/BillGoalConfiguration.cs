using EcoWattCasa.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoWattCasa.Infrastructure.Persistence.Configurations;

public sealed class BillGoalConfiguration : IEntityTypeConfiguration<BillGoal>
{
    public void Configure(EntityTypeBuilder<BillGoal> builder)
    {
        builder.ToTable("bill_goals");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(g => g.UpdatedAt).HasColumnType("timestamptz").IsRequired();
    }
}
