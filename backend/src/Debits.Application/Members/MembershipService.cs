using System.Security.Cryptography;
using Debits.Application.Abstractions;
using Debits.Domain;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Members;

public sealed record OwnerContact(Guid MemberId, Guid UserId, long TelegramUserId, AppLanguage Language);

public abstract record InviteAcceptance
{
    public sealed record Joined(Shop Shop, ShopMember Member, IReadOnlyList<OwnerContact> Owners) : InviteAcceptance;

    public sealed record AlreadyMember(Shop Shop) : InviteAcceptance;

    public sealed record Invalid : InviteAcceptance;
}

public sealed class MembershipService(IDebitsDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);
    public const string InvitePayloadPrefix = "inv_";

    /// <summary>Creates a one-time, 7-day invite. Caller checks the owner role.</summary>
    public async Task<Invite> CreateInviteAsync(Guid shopId, Guid byMemberId, MemberRole role, CancellationToken cancellationToken = default)
    {
        var invite = new Invite
        {
            ShopId = shopId,
            Token = GenerateToken(),
            Role = role,
            ExpiresAt = clock.GetUtcNow().UtcDateTime + InviteLifetime,
            MaxUses = 1,
            CreatedByMemberId = byMemberId,
        };
        db.Invites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);
        return invite;
    }

    public async Task<IReadOnlyList<Invite>> ListActiveInvitesAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Invites
            .Where(i => i.ShopId == shopId && i.RevokedAt == null && i.ExpiresAt > now && i.UsedCount < i.MaxUses)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeInviteAsync(Guid inviteId, CancellationToken cancellationToken = default)
    {
        var invite = await db.Invites.SingleAsync(i => i.Id == inviteId, cancellationToken);
        invite.RevokedAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Joins the user to the invite's shop (or re-activates a deactivated membership) and makes it the active shop.
    /// Works across tenants: must be called without a tenant set.
    /// </summary>
    public async Task<InviteAcceptance> AcceptInviteAsync(string token, Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invite = await db.Invites
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Include(i => i.Shop)
            .SingleOrDefaultAsync(i => i.Token == token, cancellationToken);
        if (invite is null || !invite.IsUsable(now))
        {
            return new InviteAcceptance.Invalid();
        }

        var existing = await db.ShopMembers
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .SingleOrDefaultAsync(m => m.ShopId == invite.ShopId && m.UserId == userId, cancellationToken);
        if (existing is { IsActive: true })
        {
            return new InviteAcceptance.AlreadyMember(invite.Shop);
        }

        ShopMember member;
        if (existing is not null)
        {
            existing.IsActive = true;
            existing.Role = invite.Role;
            member = existing;
        }
        else
        {
            member = new ShopMember { ShopId = invite.ShopId, UserId = userId, Role = invite.Role };
            db.ShopMembers.Add(member);
        }

        invite.UsedCount++;
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        user.ActiveShopId = invite.ShopId;
        await db.SaveChangesAsync(cancellationToken);

        var owners = await GetOwnersAsync(invite.ShopId, cancellationToken);
        return new InviteAcceptance.Joined(invite.Shop, member, owners.Where(o => o.UserId != userId).ToList());
    }

    public async Task<IReadOnlyList<OwnerContact>> GetOwnersAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        return await db.ShopMembers
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.ShopId == shopId && m.IsActive && m.Role == MemberRole.Owner)
            .Select(m => new OwnerContact(m.Id, m.UserId, m.User.TelegramUserId, m.User.Language))
            .ToListAsync(cancellationToken);
    }

    public static string InvitePayload(string token) => InvitePayloadPrefix + token;

    public static bool TryParseInvitePayload(string? payload, out string token)
    {
        if (payload is not null && payload.StartsWith(InvitePayloadPrefix, StringComparison.Ordinal) && payload.Length > InvitePayloadPrefix.Length)
        {
            token = payload[InvitePayloadPrefix.Length..];
            return true;
        }

        token = string.Empty;
        return false;
    }

    /// <summary>22 url-safe chars: fits Telegram's 64-char start payload with the prefix and is unguessable.</summary>
    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
