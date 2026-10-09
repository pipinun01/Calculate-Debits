namespace Debits.Domain.Entities;

public sealed class ConversationState : IHasUpdatedAt
{
    public Guid UserId { get; set; }
    public string State { get; set; } = string.Empty;
    public string Data { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}
