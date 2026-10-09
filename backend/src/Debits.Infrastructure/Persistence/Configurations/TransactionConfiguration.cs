using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transactions", t =>
        {
            t.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0");
            t.HasCheckConstraint("ck_transactions_debt_has_due_date", "type <> 'debt' OR due_date IS NOT NULL");
        });
        b.HasKey(t => t.Id);
        b.Property(t => t.Comment).HasMaxLength(512);
        b.Property(t => t.VoiceFileId).HasMaxLength(256);
        b.Property(t => t.Transcript).HasMaxLength(2048);
        b.Property(t => t.DeleteReason).HasMaxLength(256);
        b.Ignore(t => t.IsDebt);

        b.HasIndex(t => new { t.ShopId, t.CustomerId, t.CreatedAt }).IsDescending(false, false, true);
        b.HasIndex(t => new { t.ShopId, t.DueDate })
            .HasDatabaseName("ix_transactions_open_debts_due")
            .HasFilter("type = 'debt' AND NOT is_deleted");

        b.HasOne<Transaction>().WithMany().HasForeignKey(t => t.TargetDebtId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(t => t.CreatedBy).WithMany().HasForeignKey(t => t.CreatedByMemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMember>().WithMany().HasForeignKey(t => t.UpdatedByMemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMember>().WithMany().HasForeignKey(t => t.DeletedByMemberId).OnDelete(DeleteBehavior.Restrict);
    }
}
