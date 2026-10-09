namespace Debits.Domain.Entities;

/// <summary>Row belongs to a shop (tenant). Global query filter + SaveChanges enforcement rely on it.</summary>
public interface ITenantEntity
{
    Guid ShopId { get; set; }
}

public interface IHasCreatedAt
{
    DateTime CreatedAt { get; set; }
}

public interface IHasUpdatedAt
{
    DateTime UpdatedAt { get; set; }
}
