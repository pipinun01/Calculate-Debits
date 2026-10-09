namespace Debits.Domain.Entities;

public sealed class ShopMember : ITenantEntity, IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public MemberRole Role { get; set; } = MemberRole.Worker;
    public bool IsActive { get; set; } = true;
    public DateTime? NotificationsBlockedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
