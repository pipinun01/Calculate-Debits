namespace Debits.Domain.Entities;

public sealed class ReminderLogEntry
{
    public long Id { get; set; }
    public Guid TransactionId { get; set; }
    public ReminderKind Kind { get; set; }
    public DateOnly LocalDate { get; set; }
    public DateTime SentAt { get; set; }
}
