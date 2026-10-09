using Debits.Application.Abstractions;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Users;

public sealed record TelegramIdentity(long TelegramUserId, string FirstName, string? Username, string? LanguageCode);

public sealed class UserService(IDebitsDbContext db)
{
    /// <summary>Creates the user on first contact, refreshes name/username afterwards. Language is set only once, from Telegram.</summary>
    public async Task<AppUser> UpsertAsync(TelegramIdentity identity, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.TelegramUserId == identity.TelegramUserId, cancellationToken);
        if (user is null)
        {
            user = new AppUser
            {
                TelegramUserId = identity.TelegramUserId,
                Language = AppLanguageCodes.FromTelegram(identity.LanguageCode),
            };
            db.Users.Add(user);
        }

        var firstName = string.IsNullOrWhiteSpace(identity.FirstName) ? identity.TelegramUserId.ToString() : identity.FirstName.Trim();
        if (user.FirstName != firstName || user.Username != identity.Username)
        {
            user.FirstName = firstName;
            user.Username = identity.Username;
        }

        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task SetLanguageAsync(Guid userId, AppLanguage language, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        user.Language = language;
        await db.SaveChangesAsync(cancellationToken);
    }
}
