using Debits.Application.Abstractions;
using Debits.Application.Audit;
using Debits.Application.Common;
using Debits.Domain;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Shops;

public sealed record ShopMembershipInfo(Guid ShopId, string ShopName, Guid MemberId, MemberRole Role, bool IsActive);

public sealed record ShopDto(
    Guid Id,
    string Name,
    int DefaultDueDays,
    AppLanguage Language,
    string Timezone,
    ReminderRecipients ReminderRecipients,
    int ReminderHour,
    int MembersCount,
    DateTime CreatedAt);

/// <summary>Null = unchanged.</summary>
public sealed record UpdateShopCommand(
    string? Name = null,
    int? DefaultDueDays = null,
    AppLanguage? Language = null,
    string? Timezone = null,
    ReminderRecipients? ReminderRecipients = null,
    int? ReminderHour = null);

public sealed class ShopService(IDebitsDbContext db, AuditWriter audit)
{
    public const int MinDueDays = 1;
    public const int MaxDueDays = 365;
    public const int MaxNameLength = 64;

    /// <summary>Creates a shop with <paramref name="userId"/> as owner and makes it the user's active shop.</summary>
    public async Task<(Shop Shop, ShopMember Owner)> CreateAsync(Guid userId, string name, int defaultDueDays, AppLanguage language, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name);
        ValidateDueDays(defaultDueDays);

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

    public async Task<ShopDto> GetAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        return await db.Shops
            .Where(s => s.Id == shopId)
            .Select(s => new ShopDto(s.Id, s.Name, s.DefaultDueDays, s.Language, s.Timezone, s.ReminderRecipients, s.ReminderHour,
                s.Members.Count(m => m.IsActive), s.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("shop.not_found", "Shop not found");
    }

    public async Task<ShopDto> UpdateAsync(Guid shopId, UpdateShopCommand cmd, CancellationToken cancellationToken = default)
    {
        var shop = await db.Shops.SingleOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new NotFoundException("shop.not_found", "Shop not found");

        if (cmd.Name is not null)
        {
            shop.Name = ValidateName(cmd.Name);
        }

        if (cmd.DefaultDueDays is { } days)
        {
            ValidateDueDays(days);
            shop.DefaultDueDays = days;
        }

        if (cmd.Language is { } language)
        {
            shop.Language = language;
        }

        if (cmd.Timezone is not null)
        {
            if (!LocalTime.IsValidZone(cmd.Timezone))
            {
                throw new DomainException("shop.timezone_invalid", "Unknown IANA time zone");
            }

            shop.Timezone = cmd.Timezone;
        }

        if (cmd.ReminderRecipients is { } recipients)
        {
            shop.ReminderRecipients = recipients;
        }

        if (cmd.ReminderHour is { } hour)
        {
            if (hour is < 0 or > 23)
            {
                throw new DomainException("shop.reminder_hour_invalid", "Reminder hour must be 0..23");
            }

            shop.ReminderHour = hour;
        }

        audit.Write("shop.update", "shop", shop.Id, cmd);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(shopId, cancellationToken);
    }

    /// <summary>Bot-side settings update (language etc.).</summary>
    public async Task UpdateSettingsAsync(Guid shopId, Action<Shop> update, CancellationToken cancellationToken = default)
    {
        var shop = await db.Shops.SingleAsync(s => s.Id == shopId, cancellationToken);
        update(shop);
        ValidateDueDays(shop.DefaultDueDays);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new DomainException("shop.name_invalid", $"Shop name must be 1..{MaxNameLength} characters");
        }

        return name;
    }

    private static void ValidateDueDays(int days)
    {
        if (days is < MinDueDays or > MaxDueDays)
        {
            throw new DomainException("shop.due_days_invalid", $"Default due days must be {MinDueDays}..{MaxDueDays}");
        }
    }
}
