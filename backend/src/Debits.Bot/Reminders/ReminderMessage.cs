using System.Globalization;
using System.Text;
using Debits.Application.Common;
using Debits.Bot.Dispatch;
using Debits.Bot.Localization;
using Debits.Domain.Entities;

namespace Debits.Bot.Reminders;

public sealed record ReminderLine(Guid DebtId, string CustomerName, string? CustomerTag, long Remaining, string CreatedByName);

/// <summary>One grouped reminder: «shop, what is due, numbered list of debts, total».</summary>
public static class ReminderMessage
{
    /// <summary>Lines beyond this are folded into «and N more» to stay well under Telegram's 4096 characters.</summary>
    public const int MaxLines = 30;

    public static string Build(BotStrings strings, AppLanguage language, string shopName, ReminderKind kind, DateOnly dueDate, IReadOnlyList<ReminderLine> lines)
    {
        var sb = new StringBuilder();
        sb.Append(kind == ReminderKind.DueToday
            ? strings.Get(language, "reminder.today.title")
            : strings.Get(language, "reminder.in3.title", dueDate.ToString("dd.MM", CultureInfo.InvariantCulture)));
        sb.Append('\n').Append(strings.Get(language, "reminder.shop", BotContext.Esc(shopName))).Append('\n');

        var number = 0;
        foreach (var line in lines.Take(MaxLines))
        {
            var name = string.IsNullOrWhiteSpace(line.CustomerTag) ? line.CustomerName : $"{line.CustomerName} ({line.CustomerTag})";
            sb.Append('\n').Append(strings.Get(language, "reminder.line", ++number, BotContext.Esc(name), Money.Format(line.Remaining), BotContext.Esc(line.CreatedByName)));
        }

        if (lines.Count > MaxLines)
        {
            sb.Append('\n').Append(strings.Get(language, "reminder.more", lines.Count - MaxLines));
        }

        sb.Append("\n\n").Append(strings.Get(language, "reminder.total", lines.Count, Money.Format(lines.Sum(l => l.Remaining))));
        return sb.ToString();
    }
}
