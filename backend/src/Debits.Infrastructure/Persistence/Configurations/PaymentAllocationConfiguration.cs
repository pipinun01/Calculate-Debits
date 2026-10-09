using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> b)
    {
        b.ToTable("payment_allocations", t => t.HasCheckConstraint("ck_payment_allocations_amount_positive", "amount > 0"));
        b.HasKey(a => a.Id);
        b.HasIndex(a => new { a.PaymentId, a.DebtId }).IsUnique();
        b.HasIndex(a => a.DebtId);
        b.HasOne<Transaction>().WithMany().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Transaction>().WithMany().HasForeignKey(a => a.DebtId).OnDelete(DeleteBehavior.Cascade);
    }
}
