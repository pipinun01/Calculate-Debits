using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> b)
    {
        b.ToTable("shops", t =>
        {
            t.HasCheckConstraint("ck_shops_default_due_days", "default_due_days > 0");
            t.HasCheckConstraint("ck_shops_reminder_hour", "reminder_hour BETWEEN 0 AND 23");
        });
        b.HasKey(s => s.Id);
        b.Property(s => s.Name).HasMaxLength(128).IsRequired();
        b.Property(s => s.Timezone).HasMaxLength(64).IsRequired();
        b.Property(s => s.DefaultDueDays).HasDefaultValue(Shop.DefaultDueDaysDefault);
        b.Property(s => s.ReminderHour).HasDefaultValue(Shop.DefaultReminderHour);
        b.HasMany(s => s.Members).WithOne(m => m.Shop).HasForeignKey(m => m.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(s => s.Customers).WithOne(c => c.Shop).HasForeignKey(c => c.ShopId).OnDelete(DeleteBehavior.Cascade);
    }
}
