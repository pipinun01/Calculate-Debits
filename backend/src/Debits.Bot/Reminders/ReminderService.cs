using Debits.Application.Abstractions;
using Debits.Bot.Localization;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Debits.Bot.Reminders;

/// <summary>
/// Daily reminders to the shop's staff: debts due today and debts due in three days, one grouped message per kind.
/// Runs across shops, so it works without a tenant. A debt is written to <c>reminder_log</c> before anything is sent:
/// a crash or a second run can lose a message but never repeats one.
/// </summary>
public sealed class ReminderService(
    IDebitsDbContext db,
    ITelegramBotClient bot,
    BotStrings strings,
    IOptions<MiniAppOptions> miniApp,
    TimeProvider clock,
    ILogger<ReminderService> logger)
{
    public const int DaysAhead = 3;

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    private sealed record ShopInfo(Guid Id, string Name, string Timezone, int ReminderHour, ReminderRecipients Recipients);

    private sealed record Recipient(Guid MemberId, long TelegramUserId, AppLanguage Language);

    /// <summary>Sends whatever is due right now in every shop. Safe to call as often as needed.</summary>
    /// <returns>Number of messages delivered.</returns>
    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var shops = await db.Shops.AsNoTracking()
            .Select(s => new ShopInfo(s.Id, s.Name, s.Timezone, s.ReminderHour, s.ReminderRecipients))
            .ToListAsync(ct);

        var delivered = 0;
        foreach (var shop in shops)
        {
            if (ReminderSchedule.DueLocalDate(now, shop.Timezone, shop.ReminderHour) is not { } localDate)
            {
                continue;
            }

            try
            {
                delivered += await RunShopAsync(shop, localDate, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminders failed for shop {ShopId}", shop.Id);
            }
        }

        return delivered;
    }

    /// <summary>Reminders of one shop for <paramref name="localDate"/>, regardless of the current hour.</summary>
    public async Task<int> RunShopAsync(Guid shopId, DateOnly localDate, CancellationToken ct = default)
    {
        var shop = await db.Shops.AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => new ShopInfo(s.Id, s.Name, s.Timezone, s.ReminderHour, s.ReminderRecipients))
            .SingleAsync(ct);
        return await RunShopAsync(shop, localDate, ct);
    }

    private async Task<int> RunShopAsync(ShopInfo shop, DateOnly localDate, CancellationToken ct)
    {
        // Debts written after today's reminder moment wait for their own day: nobody needs a ping about an entry made a minute ago.
        var fireUtc = ReminderSchedule.FireTimeUtc(localDate, shop.Timezone, shop.ReminderHour);
        var delivered = 0;

        foreach (var kind in new[] { ReminderKind.DueToday, ReminderKind.DueIn3 })
        {
            var dueDate = kind == ReminderKind.DueToday ? localDate : localDate.AddDays(DaysAhead);
            var lines = await db.OpenDebts
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(d => d.ShopId == shop.Id && d.DueDate == dueDate && d.CreatedAt < fireUtc)
                .Where(d => !db.ReminderLog.Any(r => r.TransactionId == d.DebtId && r.Kind == kind && r.LocalDate == localDate))
                .OrderBy(d => d.CreatedAt)
                .Select(d => new
                {
                    d.DebtId,
                    Customer = db.Customers.Where(c => c.Id == d.CustomerId).Select(c => new { c.Name, c.Tag }).First(),
                    d.Remaining,
                    CreatedByName = db.ShopMembers.Where(m => m.Id == d.CreatedByMemberId).Select(m => m.User.FirstName).FirstOrDefault() ?? "",
                })
                .ToListAsync(ct);
            if (lines.Count == 0)
            {
                continue;
            }

            var sentAt = clock.GetUtcNow().UtcDateTime;
            var entries = lines.Select(l => new ReminderLogEntry { TransactionId = l.DebtId, Kind = kind, LocalDate = localDate, SentAt = sentAt }).ToList();
            db.ReminderLog.AddRange(entries);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Another run got there first (unique index on debt + kind + date).
                foreach (var entry in entries)
                {
                    db.ReminderLog.Entry(entry).State = EntityState.Detached;
                }

                logger.LogWarning(ex, "Reminder log conflict for shop {ShopId}, {Kind} on {Date}", shop.Id, kind, localDate);
                continue;
            }

            var message = lines.Select(l => new ReminderLine(l.DebtId, l.Customer.Name, l.Customer.Tag, l.Remaining, l.CreatedByName)).ToList();
            delivered += await SendAsync(shop, kind, dueDate, message, ct);
        }

        return delivered;
    }

    private async Task<int> SendAsync(ShopInfo shop, ReminderKind kind, DateOnly dueDate, IReadOnlyList<ReminderLine> lines, CancellationToken ct)
    {
        var recipients = await db.ShopMembers
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.ShopId == shop.Id && m.IsActive && m.NotificationsBlockedAt == null)
            .Where(m => shop.Recipients == ReminderRecipients.All || m.Role == MemberRole.Owner)
            .Select(m => new Recipient(m.Id, m.User.TelegramUserId, m.User.Language))
            .ToListAsync(ct);

        var delivered = 0;
        foreach (var recipient in recipients)
        {
            var text = ReminderMessage.Build(strings, recipient.Language, shop.Name, kind, dueDate, lines);
            if (await TrySendAsync(shop, recipient, text, ct))
            {
                delivered++;
            }
        }

        logger.LogInformation("Reminders {Kind} for shop {ShopId}: {Debts} debts, delivered to {Delivered} of {Recipients}", kind, shop.Id, lines.Count, delivered, recipients.Count);
        return delivered;
    }

    private async Task<bool> TrySendAsync(ShopInfo shop, Recipient recipient, string text, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await bot.SendMessage(recipient.TelegramUserId, text, parseMode: ParseMode.Html, replyMarkup: OpenDebts(shop, recipient.Language),
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
                return true;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403)
            {
                // The user blocked the bot or never started it: stop writing to them until they come back.
                var now = clock.GetUtcNow().UtcDateTime;
                await db.ShopMembers
                    .IgnoreQueryFilters([QueryFilters.Tenant])
                    .Where(m => m.Id == recipient.MemberId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.NotificationsBlockedAt, now), ct);
                logger.LogInformation("Member {MemberId} blocked the bot, reminders paused", recipient.MemberId);
                return false;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429 && attempt == 1)
            {
                var wait = TimeSpan.FromSeconds(ex.Parameters?.RetryAfter ?? 1);
                await Task.Delay(wait < MaxRetryAfter ? wait : MaxRetryAfter, clock, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Reminder to member {MemberId} was not delivered", recipient.MemberId);
                return false;
            }
        }
    }

    private InlineKeyboardMarkup? OpenDebts(ShopInfo shop, AppLanguage language)
    {
        if (!miniApp.Value.IsConfigured)
        {
            return null;
        }

        var url = $"{miniApp.Value.Url.TrimEnd('/')}/debts?shop={shop.Id}";
        return new InlineKeyboardMarkup(InlineKeyboardButton.WithWebApp(strings.Get(language, "reminder.open"), new WebAppInfo { Url = url }));
    }
}
