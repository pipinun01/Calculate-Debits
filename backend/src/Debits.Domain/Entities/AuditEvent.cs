namespace Debits.Domain.Entities;

public sealed class AuditEvent : ITenantEntity, IHasCreatedAt
{
    public long Id { get; set; }
    public Guid ShopId { get; set; }
    public Guid? MemberId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Data { get; set; }
    public DateTime CreatedAt { get; set; }
}
