using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.FirstName).HasMaxLength(128).IsRequired();
        b.Property(u => u.Username).HasMaxLength(64);
        b.HasIndex(u => u.TelegramUserId).IsUnique();
        b.HasOne(u => u.ActiveShop).WithMany().HasForeignKey(u => u.ActiveShopId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(u => u.Memberships).WithOne(m => m.User).HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
