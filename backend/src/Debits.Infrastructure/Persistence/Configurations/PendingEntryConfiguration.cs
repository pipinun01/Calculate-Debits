using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class PendingEntryConfiguration : IEntityTypeConfiguration<PendingEntry>
{
    public void Configure(EntityTypeBuilder<PendingEntry> b)
    {
        b.ToTable("pending_entries");
        b.HasKey(p => p.Id);
        b.Property(p => p.Payload).HasColumnType("jsonb").IsRequired();
        b.HasIndex(p => new { p.Status, p.ExpiresAt });
        b.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
