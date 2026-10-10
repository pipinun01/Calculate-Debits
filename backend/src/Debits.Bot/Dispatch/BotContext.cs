using System.Net;
using Debits.Application.Shops;
using Debits.Application.Tenancy;
using Debits.Bot.Localization;
using Debits.Domain.Entities;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Debits.Bot.Dispatch;

/// <summary>Everything a handler needs for one update: who, where, in which shop, in which language, plus reply helpers.</summary>
public sealed class BotContext(
    ITelegramBotClient bot,
    BotStrings strings,
    Update update,
    AppUser user,
    long chatId,
    TenantContext tenant)
{
    public ITelegramBotClient Bot { get; } = bot;
    public BotStrings Strings { get; } = strings;
    public Update Update { get; } = update;
    public AppUser User { get; } = user;
    public long ChatId { get; } = chatId;
    public TenantContext Tenant { get; } = tenant;
    public AppLanguage Language => User.Language;

    /// <summary>Membership in the active shop; null when the user has no shops (set by the dispatcher).</summary>
    public ShopMembershipInfo? Membership { get; set; }

    public Message? Message => Update.Message;
    public CallbackQuery? Callback => Update.CallbackQuery;
    public string? Text => Message?.Text;

    public string T(string key, params object?[] args) => Strings.Get(Language, key, args);

    public static string Esc(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    public Task<Message> ReplyAsync(string html, ReplyMarkup? markup = null, CancellationToken ct = default) =>
        Bot.SendMessage(ChatId, html, parseMode: ParseMode.Html, replyMarkup: markup, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);

    public Task AnswerCallbackAsync(string? text = null, CancellationToken ct = default) =>
        Callback is null ? Task.CompletedTask : Bot.AnswerCallbackQuery(Callback.Id, text, cancellationToken: ct);

    /// <summary>Edits the message the callback came from (falls back to a new message when there is none).</summary>
    public async Task EditOrReplyAsync(string html, InlineKeyboardMarkup? markup = null, CancellationToken ct = default)
    {
        if (Callback?.Message is { } m)
        {
            try
            {
                await Bot.EditMessageText(m.Chat.Id, m.Id, html, parseMode: ParseMode.Html, replyMarkup: markup, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
            {
                // A second tap on the same button: nothing to change.
            }

            return;
        }

        await ReplyAsync(html, markup, ct);
    }
}
