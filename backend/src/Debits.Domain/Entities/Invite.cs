namespace Debits.Domain.Entities;

public sealed class Invite : ITenantEntity, IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public string Token { get; set; } = string.Empty;
    public MemberRole Role { get; set; } = MemberRole.Worker;
    public DateTime ExpiresAt { get; set; }
    public int MaxUses { get; set; } = 1;
    public int UsedCount { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? CreatedByMemberId { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsUsable(DateTime utcNow) =>
        RevokedAt is null && ExpiresAt > utcNow && UsedCount < MaxUses;
}
