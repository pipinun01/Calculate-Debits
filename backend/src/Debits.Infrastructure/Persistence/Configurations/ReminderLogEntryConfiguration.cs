using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class ReminderLogEntryConfiguration : IEntityTypeConfiguration<ReminderLogEntry>
{
    public void Configure(EntityTypeBuilder<ReminderLogEntry> b)
    {
        b.ToTable("reminder_log");
        b.HasKey(r => r.Id);
        b.HasIndex(r => new { r.TransactionId, r.Kind, r.LocalDate }).IsUnique();
        b.HasOne<Transaction>().WithMany().HasForeignKey(r => r.TransactionId).OnDelete(DeleteBehavior.Cascade);
    }
}
