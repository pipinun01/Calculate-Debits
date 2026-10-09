using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("audit_events");
        b.HasKey(a => a.Id);
        b.Property(a => a.Action).HasMaxLength(64).IsRequired();
        b.Property(a => a.Entity).HasMaxLength(64).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(64).IsRequired();
        b.Property(a => a.Data).HasColumnType("jsonb");
        b.HasIndex(a => new { a.ShopId, a.CreatedAt });
        b.HasOne<Shop>().WithMany().HasForeignKey(a => a.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ShopMember>().WithMany().HasForeignKey(a => a.MemberId).OnDelete(DeleteBehavior.SetNull);
    }
}
