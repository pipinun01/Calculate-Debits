using Debits.Application.Members;
using Debits.Application.Shops;
using Debits.Bot.Conversation;
using Debits.Bot.Dispatch;
using Debits.Domain.Entities;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Debits.Bot.Handlers;

/// <summary>/start: invite deep link → join; no shops → shop wizard; otherwise the menu. Also drives the wizard steps.</summary>
public sealed class StartHandler(
    ShopService shops,
    MembershipService members,
    ConversationStore conversation,
    MenuHandler menu,
    ILogger<StartHandler> logger)
{
    public const string WizardPrefix = "shop_wizard:";
    public const string StepName = WizardPrefix + "name";
    public const string StepDue = WizardPrefix + "due";
    public const string StepDueCustom = WizardPrefix + "due_custom";
    public const string StepLang = WizardPrefix + "lang";

    public sealed class WizardData
    {
        public string? Name { get; set; }
        public int? DueDays { get; set; }
    }

    public async Task HandleStartAsync(BotContext ctx, string? payload, CancellationToken ct)
    {
        if (MembershipService.TryParseInvitePayload(payload, out var token))
        {
            await AcceptInviteAsync(ctx, token, ct);
            return;
        }

        var memberships = await shops.ListForUserAsync(ctx.User.Id, ct);
        if (memberships.Count == 0)
        {
            await BeginWizardAsync(ctx, ct);
            return;
        }

        ctx.Membership = await shops.ResolveActiveAsync(ctx.User.Id, ct);
        await menu.ShowAsync(ctx, ct);
    }

    public async Task BeginWizardAsync(BotContext ctx, CancellationToken ct)
    {
        await conversation.SetAsync(ctx.User.Id, StepName, new WizardData(), ct);
        await ctx.ReplyAsync(ctx.T("start.welcome", BotContext.Esc(ctx.User.FirstName)), ct: ct);
    }

    /// <summary>Text message while the wizard is active. Returns false when the user is not in the wizard.</summary>
    public async Task<bool> HandleWizardTextAsync(BotContext ctx, string text, CancellationToken ct)
    {
        var current = await conversation.GetAsync<WizardData>(ctx.User.Id, WizardPrefix, ct);
        if (current is null)
        {
            return false;
        }

        var (state, data) = current.Value;
        switch (state)
        {
            case StepName:
                var name = text.Trim();
                if (name.Length is 0 or > ShopService.MaxNameLength)
                {
                    await ctx.ReplyAsync(ctx.T("wizard.name_invalid"), ct: ct);
                    return true;
                }

                data.Name = name;
                await conversation.SetAsync(ctx.User.Id, StepDue, data, ct);
                await ctx.ReplyAsync(ctx.T("wizard.ask_due", BotContext.Esc(name)), Keyboards.DueDays(ctx), ct);
                return true;

            case StepDueCustom:
                if (!int.TryParse(text.Trim(), out var days) || days is < ShopService.MinDueDays or > ShopService.MaxDueDays)
                {
                    await ctx.ReplyAsync(ctx.T("wizard.due_invalid"), ct: ct);
                    return true;
                }

                await SetDueAndAskLanguageAsync(ctx, data, days, ct);
                return true;

            case StepDue:
                await ctx.ReplyAsync(ctx.T("wizard.ask_due", BotContext.Esc(data.Name)), Keyboards.DueDays(ctx), ct);
                return true;

            case StepLang:
                await ctx.ReplyAsync(ctx.T("wizard.ask_lang"), Keyboards.Languages(ctx, Callbacks.WizardLang), ct);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Callback <c>w:due:*</c> / <c>w:lang:*</c>.</summary>
    public async Task HandleWizardCallbackAsync(BotContext ctx, string data, CancellationToken ct)
    {
        var current = await conversation.GetAsync<WizardData>(ctx.User.Id, WizardPrefix, ct);
        if (current is null)
        {
            await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
            return;
        }

        var (state, wizard) = current.Value;

        if (data.StartsWith(Callbacks.WizardDue, StringComparison.Ordinal) && state == StepDue)
        {
            var value = data[Callbacks.WizardDue.Length..];
            await ctx.AnswerCallbackAsync(ct: ct);
            if (value == "custom")
            {
                await conversation.SetAsync(ctx.User.Id, StepDueCustom, wizard, ct);
                await ctx.EditOrReplyAsync(ctx.T("wizard.ask_due_custom"), ct: ct);
                return;
            }

            if (int.TryParse(value, out var days) && days is >= ShopService.MinDueDays and <= ShopService.MaxDueDays)
            {
                await SetDueAndAskLanguageAsync(ctx, wizard, days, ct, edit: true);
            }

            return;
        }

        if (data.StartsWith(Callbacks.WizardLang, StringComparison.Ordinal) && state == StepLang && wizard.Name is not null && wizard.DueDays is not null)
        {
            var language = AppLanguageCodes.FromCode(data[Callbacks.WizardLang.Length..]);
            ctx.User.Language = language;
            var (shop, _) = await shops.CreateAsync(ctx.User.Id, wizard.Name, wizard.DueDays.Value, language, ct);
            await conversation.ClearAsync(ctx.User.Id, ct);
            ctx.Membership = await shops.ResolveActiveAsync(ctx.User.Id, ct);

            await ctx.AnswerCallbackAsync(ct: ct);
            await ctx.EditOrReplyAsync(ctx.T("wizard.done", BotContext.Esc(shop.Name), shop.DefaultDueDays), ct: ct);
            await menu.ShowAsync(ctx, ct);
            return;
        }

        await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
    }

    private async Task SetDueAndAskLanguageAsync(BotContext ctx, WizardData data, int days, CancellationToken ct, bool edit = false)
    {
        data.DueDays = days;
        await conversation.SetAsync(ctx.User.Id, StepLang, data, ct);
        var text = ctx.T("wizard.ask_lang");
        var markup = Keyboards.Languages(ctx, Callbacks.WizardLang);
        if (edit)
        {
            await ctx.EditOrReplyAsync(text, markup, ct);
        }
        else
        {
            await ctx.ReplyAsync(text, markup, ct);
        }
    }

    private async Task AcceptInviteAsync(BotContext ctx, string token, CancellationToken ct)
    {
        var result = await members.AcceptInviteAsync(token, ctx.User.Id, ct);
        switch (result)
        {
            case InviteAcceptance.Invalid:
                await ctx.ReplyAsync(ctx.T("invite.invalid"), ct: ct);
                break;

            case InviteAcceptance.AlreadyMember already:
                await ctx.ReplyAsync(ctx.T("invite.already_member", BotContext.Esc(already.Shop.Name)), ct: ct);
                ctx.Membership = await shops.ResolveActiveAsync(ctx.User.Id, ct);
                await menu.ShowAsync(ctx, ct);
                break;

            case InviteAcceptance.Joined joined:
                await conversation.ClearAsync(ctx.User.Id, ct);
                ctx.Membership = await shops.ResolveActiveAsync(ctx.User.Id, ct);
                await ctx.ReplyAsync(ctx.T("invite.joined", BotContext.Esc(joined.Shop.Name), ctx.T("role." + RoleKey(joined.Member.Role))), ct: ct);
                await menu.ShowAsync(ctx, ct);
                await NotifyOwnersAsync(ctx, joined, ct);
                break;
        }
    }

    private async Task NotifyOwnersAsync(BotContext ctx, InviteAcceptance.Joined joined, CancellationToken ct)
    {
        foreach (var owner in joined.Owners)
        {
            try
            {
                var text = StringsFor(ctx, owner.Language, "invite.owner_notified",
                    BotContext.Esc(ctx.User.FirstName), BotContext.Esc(joined.Shop.Name), StringsFor(ctx, owner.Language, "role." + RoleKey(joined.Member.Role)));
                await ctx.Bot.SendMessage(owner.TelegramUserId, text, parseMode: ParseMode.Html, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not notify owner {TelegramUserId} about new member", owner.TelegramUserId);
            }
        }
    }

    private static string RoleKey(MemberRole role) => role.ToString().ToLowerInvariant();

    private static string StringsFor(BotContext ctx, AppLanguage language, string key, params object?[] args) =>
        ctx.Strings.Get(language, key, args);
}
