using System.Security.Cryptography;
using Debits.Application.Abstractions;
using Debits.Application.Audit;
using Debits.Application.Common;
using Debits.Domain;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Members;

public sealed record OwnerContact(Guid MemberId, Guid UserId, long TelegramUserId, AppLanguage Language);

public sealed record MemberDto(
    Guid Id,
    Guid UserId,
    long TelegramUserId,
    string FirstName,
    string? Username,
    MemberRole Role,
    bool IsActive,
    bool NotificationsBlocked,
    DateTime CreatedAt);

public sealed record InviteDto(Guid Id, string Token, MemberRole Role, DateTime ExpiresAt, int MaxUses, int UsedCount, DateTime CreatedAt, string? CreatedByName);

public abstract record InviteAcceptance
{
    public sealed record Joined(Shop Shop, ShopMember Member, IReadOnlyList<OwnerContact> Owners) : InviteAcceptance;

    public sealed record AlreadyMember(Shop Shop) : InviteAcceptance;

    public sealed record Invalid : InviteAcceptance;
}

public sealed class MembershipService(IDebitsDbContext db, TimeProvider clock, AuditWriter audit)
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

    public async Task<IReadOnlyList<InviteDto>> ListActiveInviteDtosAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Invites
            .Where(i => i.ShopId == shopId && i.RevokedAt == null && i.ExpiresAt > now && i.UsedCount < i.MaxUses)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InviteDto(i.Id, i.Token, i.Role, i.ExpiresAt, i.MaxUses, i.UsedCount, i.CreatedAt,
                db.ShopMembers.Where(m => m.Id == i.CreatedByMemberId).Select(m => m.User.FirstName).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public static InviteDto ToDto(Invite invite, string? createdByName) =>
        new(invite.Id, invite.Token, invite.Role, invite.ExpiresAt, invite.MaxUses, invite.UsedCount, invite.CreatedAt, createdByName);

    public async Task RevokeInviteAsync(Guid inviteId, CancellationToken cancellationToken = default)
    {
        var invite = await db.Invites.SingleOrDefaultAsync(i => i.Id == inviteId, cancellationToken)
            ?? throw new NotFoundException("invite.not_found", "Invite not found");
        invite.RevokedAt ??= clock.GetUtcNow().UtcDateTime;
        audit.Write("invite.revoke", "invite", invite.Id);
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

    /// <summary>Active members of the current shop, owners first.</summary>
    /// <summary>The user wrote to the bot, so the bot can write back: resumes reminders paused after a 403 from Telegram.</summary>
    public Task<int> MarkReachableAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.ShopMembers
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.UserId == userId && m.NotificationsBlockedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NotificationsBlockedAt, (DateTime?)null), cancellationToken);

    public async Task<IReadOnlyList<MemberDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await db.ShopMembers
            .Where(m => m.IsActive)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.CreatedAt)
            .Select(m => new MemberDto(m.Id, m.UserId, m.User.TelegramUserId, m.User.FirstName, m.User.Username, m.Role, m.IsActive, m.NotificationsBlockedAt != null, m.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Changes a member's role; the last active owner cannot be demoted.</summary>
    public async Task<MemberDto> ChangeRoleAsync(Guid memberId, MemberRole role, CancellationToken cancellationToken = default)
    {
        var member = await db.ShopMembers.Include(m => m.User).SingleOrDefaultAsync(m => m.Id == memberId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("member.not_found", "Member not found");

        if (member.Role == MemberRole.Owner && role != MemberRole.Owner)
        {
            await EnsureNotLastOwnerAsync(member, cancellationToken);
        }

        member.Role = role;
        audit.Write("member.role", "member", member.Id, new { role });
        await db.SaveChangesAsync(cancellationToken);
        return new MemberDto(member.Id, member.UserId, member.User.TelegramUserId, member.User.FirstName, member.User.Username, member.Role, member.IsActive, member.NotificationsBlockedAt != null, member.CreatedAt);
    }

    /// <summary>Deactivates a membership (the row stays for history). The last active owner cannot leave.</summary>
    public async Task DeactivateAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var member = await db.ShopMembers.Include(m => m.User).SingleOrDefaultAsync(m => m.Id == memberId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("member.not_found", "Member not found");

        if (member.Role == MemberRole.Owner)
        {
            await EnsureNotLastOwnerAsync(member, cancellationToken);
        }

        member.IsActive = false;
        if (member.User.ActiveShopId == member.ShopId)
        {
            member.User.ActiveShopId = null;
        }

        audit.Write("member.deactivate", "member", member.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureNotLastOwnerAsync(ShopMember member, CancellationToken cancellationToken)
    {
        var otherOwners = await db.ShopMembers.CountAsync(m => m.ShopId == member.ShopId && m.IsActive && m.Role == MemberRole.Owner && m.Id != member.Id, cancellationToken);
        if (otherOwners == 0)
        {
            throw new ConflictException("member.last_owner", "The shop must keep at least one owner");
        }
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
