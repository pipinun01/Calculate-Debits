namespace Debits.Domain.Entities;

/// <summary>Voice/text entry awaiting confirmation in the bot. bigserial id keeps callback_data short.</summary>
public sealed class PendingEntry : ITenantEntity, IHasCreatedAt
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public long Id { get; set; }
    public Guid ShopId { get; set; }
    public Guid UserId { get; set; }
    public long ChatId { get; set; }
    public int MessageId { get; set; }
    public string Payload { get; set; } = "{}";
    public PendingEntryStatus Status { get; set; } = PendingEntryStatus.Pending;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
