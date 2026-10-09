using Debits.Domain.Text;

namespace Debits.Domain.Entities;

public sealed class Customer : ITenantEntity, IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Tag { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public string SearchKey { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public Guid? CreatedByMemberId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Tag) ? Name : $"{Name} ({Tag})";

    public void RefreshSearchKey() => SearchKey = Text.SearchKey.Normalize(Name, Tag);

    public static Customer Create(Guid shopId, string name, string? tag, Guid? createdByMemberId, string? phone = null)
    {
        var customer = new Customer
        {
            ShopId = shopId,
            Name = name.Trim(),
            Tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CreatedByMemberId = createdByMemberId,
        };
        customer.RefreshSearchKey();
        return customer;
    }
}
