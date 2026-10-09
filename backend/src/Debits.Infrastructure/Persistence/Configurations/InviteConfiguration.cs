using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> b)
    {
        b.ToTable("invites");
        b.HasKey(i => i.Id);
        b.Property(i => i.Token).HasMaxLength(64).IsRequired();
        b.HasIndex(i => i.Token).IsUnique();
        b.HasOne(i => i.Shop).WithMany().HasForeignKey(i => i.ShopId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ShopMember>().WithMany().HasForeignKey(i => i.CreatedByMemberId).OnDelete(DeleteBehavior.SetNull);
    }
}
