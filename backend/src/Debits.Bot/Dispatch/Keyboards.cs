using Debits.Application.Shops;
using Debits.Domain.Entities;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Debits.Bot.Dispatch;

public static class Callbacks
{
    public const string WizardDue = "w:due:";
    public const string WizardLang = "w:lang:";
    public const string SwitchShop = "shop:";
    public const string NewShop = "shop:new";
    public const string SetLang = "lang:";
}

public static class Keyboards
{
    public static readonly int[] DuePresets = [15, 30, 45];

    public static InlineKeyboardMarkup DueDays(BotContext ctx)
    {
        var row = DuePresets.Select(d => InlineKeyboardButton.WithCallbackData(ctx.T("wizard.due.days", d), Callbacks.WizardDue + d)).ToList();
        row.Add(InlineKeyboardButton.WithCallbackData(ctx.T("wizard.due.custom"), Callbacks.WizardDue + "custom"));
        return new InlineKeyboardMarkup(row);
    }

    public static InlineKeyboardMarkup Languages(BotContext ctx, string prefix) =>
        new(Enum.GetValues<AppLanguage>().Select(l =>
            new[] { InlineKeyboardButton.WithCallbackData(ctx.T("lang." + l.ToCode()), prefix + l.ToCode()) }));

    public static InlineKeyboardMarkup Shops(BotContext ctx, IReadOnlyList<ShopMembershipInfo> shops)
    {
        var rows = shops.Select(s => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"{(s.IsActive ? "✅ " : string.Empty)}{s.ShopName} · {ctx.T("role." + s.Role.ToString().ToLowerInvariant())}",
                Callbacks.SwitchShop + s.ShopId.ToString("N")),
        }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData(ctx.T("shops.create"), Callbacks.NewShop)]);
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup? OpenApp(BotContext ctx, string miniAppUrl) =>
        string.IsNullOrWhiteSpace(miniAppUrl)
            ? null
            : new InlineKeyboardMarkup(InlineKeyboardButton.WithWebApp(ctx.T("menu.open_app"), new WebAppInfo { Url = miniAppUrl }));

    public static InlineKeyboardMarkup Share(string label, string url, string text) =>
        new(InlineKeyboardButton.WithUrl(label, $"https://t.me/share/url?url={Uri.EscapeDataString(url)}&text={Uri.EscapeDataString(text)}"));
}
