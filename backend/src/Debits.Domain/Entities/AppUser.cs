namespace Debits.Domain.Entities;

public sealed class AppUser : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public long TelegramUserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public AppLanguage Language { get; set; } = AppLanguage.UzLatn;
    public Guid? ActiveShopId { get; set; }
    public Shop? ActiveShop { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ShopMember> Memberships { get; set; } = [];
}
