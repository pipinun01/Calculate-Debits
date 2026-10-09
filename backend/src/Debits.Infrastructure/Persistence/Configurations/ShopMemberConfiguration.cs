using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class ShopMemberConfiguration : IEntityTypeConfiguration<ShopMember>
{
    public void Configure(EntityTypeBuilder<ShopMember> b)
    {
        b.ToTable("shop_members");
        b.HasKey(m => m.Id);
        b.HasIndex(m => new { m.ShopId, m.UserId }).IsUnique();
        b.HasIndex(m => m.UserId);
    }
}
