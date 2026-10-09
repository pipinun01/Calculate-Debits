using Debits.Application.Abstractions;
using Debits.Domain;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Shops;

public sealed record ShopMembershipInfo(Guid ShopId, string ShopName, Guid MemberId, MemberRole Role, bool IsActive);

public sealed class ShopService(IDebitsDbContext db)
{
    public const int MinDueDays = 1;
    public const int MaxDueDays = 365;
    public const int MaxNameLength = 64;

    /// <summary>Creates a shop with <paramref name="userId"/> as owner and makes it the user's active shop.</summary>
    public async Task<(Shop Shop, ShopMember Owner)> CreateAsync(Guid userId, string name, int defaultDueDays, AppLanguage language, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new DomainException("shop.name_invalid", $"Shop name must be 1..{MaxNameLength} characters");
        }

        if (defaultDueDays is < MinDueDays or > MaxDueDays)
        {
            throw new DomainException("shop.due_days_invalid", $"Default due days must be {MinDueDays}..{MaxDueDays}");
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        var shop = new Shop { Name = name, DefaultDueDays = defaultDueDays, Language = language };
        var owner = new ShopMember { Shop = shop, ShopId = shop.Id, UserId = userId, Role = MemberRole.Owner };
        db.Shops.Add(shop);
        db.ShopMembers.Add(owner);
        user.ActiveShopId = shop.Id;
        await db.SaveChangesAsync(cancellationToken);
        return (shop, owner);
    }

    /// <summary>All shops the user is an active member of, regardless of the current tenant.</summary>
    public async Task<IReadOnlyList<ShopMembershipInfo>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var activeShopId = await db.Users.Where(u => u.Id == userId).Select(u => u.ActiveShopId).SingleAsync(cancellationToken);
        return await db.ShopMembers
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.UserId == userId && m.IsActive)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ShopMembershipInfo(m.ShopId, m.Shop.Name, m.Id, m.Role, m.ShopId == activeShopId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Switches the active shop; the user must be an active member of it.</summary>
    public async Task<ShopMembershipInfo> SetActiveAsync(Guid userId, Guid shopId, CancellationToken cancellationToken = default)
    {
        var membership = await db.ShopMembers
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.UserId == userId && m.ShopId == shopId && m.IsActive)
            .Select(m => new ShopMembershipInfo(m.ShopId, m.Shop.Name, m.Id, m.Role, true))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("shop.not_member", "User is not a member of this shop");

        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        user.ActiveShopId = shopId;
        await db.SaveChangesAsync(cancellationToken);
        return membership;
    }

    /// <summary>Membership in the user's active shop, picking the first shop when none is active yet. Null when the user has no shops.</summary>
    public async Task<ShopMembershipInfo?> ResolveActiveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var all = await ListForUserAsync(userId, cancellationToken);
        if (all.Count == 0)
        {
            return null;
        }

        var active = all.FirstOrDefault(m => m.IsActive);
        if (active is not null)
        {
            return active;
        }

        return await SetActiveAsync(userId, all[0].ShopId, cancellationToken);
    }

    public async Task UpdateSettingsAsync(Guid shopId, Action<Shop> update, CancellationToken cancellationToken = default)
    {
        var shop = await db.Shops.SingleAsync(s => s.Id == shopId, cancellationToken);
        update(shop);
        if (shop.DefaultDueDays is < MinDueDays or > MaxDueDays)
        {
            throw new DomainException("shop.due_days_invalid", $"Default due days must be {MinDueDays}..{MaxDueDays}");
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
