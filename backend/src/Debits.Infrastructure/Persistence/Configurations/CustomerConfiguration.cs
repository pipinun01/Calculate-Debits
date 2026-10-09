using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(c => c.Id);
        b.Property(c => c.Name).HasMaxLength(128).IsRequired();
        b.Property(c => c.Tag).HasMaxLength(64);
        b.Property(c => c.Phone).HasMaxLength(32);
        b.Property(c => c.Notes).HasMaxLength(1024);
        b.Property(c => c.SearchKey).HasMaxLength(256).IsRequired();
        b.Ignore(c => c.DisplayName);

        b.HasIndex(c => new { c.ShopId, c.SearchKey }).IsUnique();
        b.HasIndex(c => c.SearchKey)
            .HasDatabaseName("ix_customers_search_key_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        b.HasOne<ShopMember>().WithMany().HasForeignKey(c => c.CreatedByMemberId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(c => c.Transactions).WithOne(t => t.Customer).HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}
