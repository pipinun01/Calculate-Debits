using Debits.Domain.Entities;

namespace Debits.Application.Tenancy;

/// <summary>
/// Who is acting and in which shop. Filled per request by the API middleware (JWT + X-Shop-Id)
/// or by the bot from <c>users.active_shop_id</c>. <see cref="ShopId"/> is null for background work that spans shops.
/// </summary>
public interface ITenantContext
{
    Guid? ShopId { get; }
    Guid? MemberId { get; }
    Guid? UserId { get; }
    MemberRole? Role { get; }
    AppLanguage Language { get; }

    bool IsOwner => Role == MemberRole.Owner;
}

/// <summary>Scoped, mutable implementation. Resolvers call <see cref="Set"/>; everyone else reads <see cref="ITenantContext"/>.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? ShopId { get; private set; }
    public Guid? MemberId { get; private set; }
    public Guid? UserId { get; private set; }
    public MemberRole? Role { get; private set; }
    public AppLanguage Language { get; private set; } = AppLanguage.UzLatn;

    public void Set(Guid? shopId, Guid? memberId, Guid? userId, MemberRole? role, AppLanguage language)
    {
        ShopId = shopId;
        MemberId = memberId;
        UserId = userId;
        Role = role;
        Language = language;
    }

    public void SetShop(Guid shopId) => ShopId = shopId;

    public void Clear() => Set(null, null, null, null, AppLanguage.UzLatn);
}
