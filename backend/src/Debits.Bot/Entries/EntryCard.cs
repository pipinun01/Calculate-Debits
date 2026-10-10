using System.Globalization;
using System.Text;
using Debits.Application.Common;
using Debits.Application.Entries;
using Debits.Bot.Dispatch;
using Debits.Domain.Entities;
using Telegram.Bot.Types.ReplyMarkups;

namespace Debits.Bot.Entries;

/// <summary>Text and keyboards of the confirmation card. callback_data: <c>pe:{id}:{action}[:{arg}]</c>, always well under 64 bytes.</summary>
public static class EntryCard
{
    public const string Prefix = "pe:";
    public const int MaxTranscriptShown = 300;

    public static string Data(long id, string action, string? arg = null) => arg is null ? $"{Prefix}{id}:{action}" : $"{Prefix}{id}:{action}:{arg}";

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string Render(BotContext ctx, PendingPayload p, string? footerKey = "entry.confirm_hint")
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(p.Transcript))
        {
            var heard = p.Transcript.Length > MaxTranscriptShown ? p.Transcript[..MaxTranscriptShown] + "…" : p.Transcript;
            sb.AppendLine(ctx.T("entry.heard", BotContext.Esc(heard)));
            sb.AppendLine();
        }

        AppendBody(sb, ctx, p);

        foreach (var warning in p.Warnings)
        {
            var key = warning switch
            {
                RegexEntryParser.WarnAmountSmall => "warn.amount_small",
                RegexEntryParser.WarnAmountMismatch => "warn.amount_mismatch",
                _ => null,
            };
            if (key is not null && p.Amount is not null)
            {
                sb.AppendLine(ctx.T(key));
            }
        }

        if (footerKey is not null)
        {
            sb.AppendLine();
            sb.Append(ctx.T(footerKey));
        }

        return sb.ToString().TrimEnd();
    }

    public static string Receipt(BotContext ctx, PendingPayload p, string authorName, long balance)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ctx.T(p.Type == TransactionType.Debt ? "receipt.debt" : "receipt.payment"));
        sb.AppendLine();
        AppendBody(sb, ctx, p, includeType: false);
        sb.AppendLine(ctx.T("receipt.by", BotContext.Esc(authorName)));
        sb.AppendLine();
        sb.Append(ctx.T("receipt.balance", BotContext.Esc(p.CustomerDisplay), Money.Format(balance)));
        return sb.ToString();
    }

    private static void AppendBody(StringBuilder sb, BotContext ctx, PendingPayload p, bool includeType = true)
    {
        sb.AppendLine(ctx.T("entry.shop", BotContext.Esc(p.ShopName)));
        sb.AppendLine(p switch
        {
            { CustomerId: not null } => ctx.T("entry.customer", BotContext.Esc(p.CustomerDisplay)),
            { HasCustomer: true } => ctx.T("entry.customer_new", BotContext.Esc(p.CustomerDisplay)),
            _ => ctx.T("entry.customer_unknown"),
        });
        if (includeType)
        {
            sb.AppendLine(ctx.T(p.Type == TransactionType.Debt ? "entry.type.debt" : "entry.type.payment"));
        }

        sb.AppendLine(p.Amount is { } amount ? ctx.T("entry.amount", Money.Format(amount)) : ctx.T("entry.amount_unknown"));
        if (p.Type == TransactionType.Debt && p.DueDate is { } due)
        {
            sb.AppendLine(ctx.T("entry.due", Date(due)));
        }

        if (!string.IsNullOrWhiteSpace(p.Comment))
        {
            sb.AppendLine(ctx.T("entry.comment", BotContext.Esc(p.Comment)));
        }
    }

    public static InlineKeyboardMarkup Main(BotContext ctx, long id, PendingPayload p)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { Button(ctx, "btn.ok", id, "ok") },
            new[] { Button(ctx, "btn.customer", id, "cu"), Button(ctx, "btn.amount", id, "amt") },
        };
        rows.Add(p.Type == TransactionType.Debt
            ? [Button(ctx, "btn.due", id, "due"), Button(ctx, "btn.type", id, "type")]
            : [Button(ctx, "btn.type", id, "type")]);
        rows.Add([Button(ctx, "btn.cancel", id, "x")]);
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup Customers(BotContext ctx, long id, PendingPayload p)
    {
        var rows = p.Candidates
            .Select((c, i) => new[] { InlineKeyboardButton.WithCallbackData(Cut(c.Display, 48), Data(id, "cu", i.ToString(CultureInfo.InvariantCulture))) })
            .ToList();
        if (!string.IsNullOrWhiteSpace(p.SpokenName))
        {
            rows.Add([InlineKeyboardButton.WithCallbackData(Cut(ctx.T("btn.new_customer", p.SpokenName), 60), Data(id, "cu", "new"))]);
        }

        rows.Add([Button(ctx, "btn.type_name", id, "cu", "type")]);
        rows.Add(p.HasCustomer ? [Button(ctx, "btn.back", id, "back"), Button(ctx, "btn.cancel", id, "x")] : [Button(ctx, "btn.cancel", id, "x")]);
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup Due(BotContext ctx, long id) => new(
    [
        [Button(ctx, "due.3", id, "due", "3"), Button(ctx, "due.7", id, "due", "7"), Button(ctx, "due.30", id, "due", "30")],
        [Button(ctx, "due.eom", id, "due", "eom"), Button(ctx, "due.pick", id, "due", "pick")],
        [Button(ctx, "btn.back", id, "back")],
    ]);

    public static InlineKeyboardMarkup Back(BotContext ctx, long id) => new(Button(ctx, "btn.back", id, "back"));

    public static InlineKeyboardMarkup SkipTag(BotContext ctx, long id) => new(
    [
        [Button(ctx, "btn.skip", id, "tag", "skip")],
        [Button(ctx, "btn.back", id, "cu")],
    ]);

    public static InlineKeyboardMarkup Delete(BotContext ctx, long id) => new(Button(ctx, "btn.delete", id, "del"));

    private static InlineKeyboardButton Button(BotContext ctx, string key, long id, string action, string? arg = null) =>
        InlineKeyboardButton.WithCallbackData(ctx.T(key), Data(id, action, arg));

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
