using Debits.Application.Members;
using Debits.Application.Shops;
using Debits.Application.Tenancy;
using Debits.Application.Users;
using Debits.Bot.Conversation;
using Debits.Bot.Handlers;
using Debits.Bot.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Debits.Bot.Dispatch;

/// <summary>Entry point for every update (polling or webhook): one DI scope, user upsert, tenant resolution, routing.</summary>
public sealed class UpdateDispatcher(IServiceScopeFactory scopeFactory, ILogger<UpdateDispatcher> logger)
{
    public async Task DispatchAsync(Update update, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<UpdateRouter>().RouteAsync(update, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in update {UpdateId}", update.Id);
            await TrySendErrorAsync(scope.ServiceProvider, update, ct);
        }
    }

    private static async Task TrySendErrorAsync(IServiceProvider services, Update update, CancellationToken ct)
    {
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
        if (chatId is null)
        {
            return;
        }

        try
        {
            var strings = services.GetRequiredService<BotStrings>();
            var language = Domain.Entities.AppLanguageCodes.FromTelegram(update.Message?.From?.LanguageCode ?? update.CallbackQuery?.From.LanguageCode);
            await services.GetRequiredService<ITelegramBotClient>().SendMessage(chatId.Value, strings.Get(language, "error.generic"), cancellationToken: ct);
        }
        catch
        {
            // best effort
        }
    }
}

/// <summary>Scoped routing of a single update.</summary>
public sealed class UpdateRouter(
    ITelegramBotClient bot,
    BotStrings strings,
    TenantContext tenant,
    UserService users,
    ShopService shops,
    MembershipService members,
    ConversationStore conversation,
    StartHandler start,
    MenuHandler menu,
    ShopsHandler shopsHandler,
    LanguageHandler language,
    InviteHandler invite,
    EntryHandler entries,
    VoiceHandler voice,
    ILogger<UpdateRouter> logger)
{
    public async Task RouteAsync(Update update, CancellationToken ct)
    {
        var (from, chatId) = update switch
        {
            { Message: { From: { } f, Chat: { Type: ChatType.Private } c } } => (f, c.Id),
            { CallbackQuery: { From: { } f, Message: { Chat: { } c } } } => (f, c.Id),
            _ => (null, 0L),
        };
        if (from is null || from.IsBot)
        {
            logger.LogDebug("Ignoring update {UpdateId} of type {Type}", update.Id, update.Type);
            return;
        }

        var user = await users.UpsertAsync(new TelegramIdentity(from.Id, from.FirstName, from.Username, from.LanguageCode), ct);
        await members.MarkReachableAsync(user.Id, ct);
        var ctx = new BotContext(bot, strings, update, user, chatId, tenant);

        if (update.CallbackQuery is { Data: { } data })
        {
            await ResolveTenantAsync(ctx, ct);
            await RouteCallbackAsync(ctx, data, ct);
            return;
        }

        var text = update.Message?.Text;
        if (text is not null && text.StartsWith('/'))
        {
            await RouteCommandAsync(ctx, text, ct);
            return;
        }

        if (text is not null && await start.HandleWizardTextAsync(ctx, text, ct))
        {
            return;
        }

        await ResolveTenantAsync(ctx, ct);
        if (ctx.Membership is null)
        {
            await start.BeginWizardAsync(ctx, ct);
            return;
        }

        var message = update.Message;
        if (message?.Voice is { } v)
        {
            await voice.HandleAsync(ctx, v.FileId, v.Duration, "voice.ogg", ct);
            return;
        }

        if (message?.Audio is { } a)
        {
            await voice.HandleAsync(ctx, a.FileId, a.Duration, string.IsNullOrWhiteSpace(a.FileName) ? "audio.mp3" : a.FileName, ct);
            return;
        }

        if (text is not null && (await entries.HandleStateTextAsync(ctx, text, ct) || await entries.StartAsync(ctx, text, Domain.Entities.EntrySource.Text, null, ct)))
        {
            return;
        }

        await ctx.ReplyAsync(ctx.T("fallback.text"), ct: ct);
    }

    private async Task RouteCommandAsync(BotContext ctx, string text, CancellationToken ct)
    {
        var space = text.IndexOf(' ');
        var command = (space < 0 ? text : text[..space]).ToLowerInvariant();
        var at = command.IndexOf('@');
        if (at > 0)
        {
            command = command[..at];
        }

        var args = space < 0 ? null : text[(space + 1)..].Trim();

        if (command == "/cancel")
        {
            var cleared = await conversation.ClearAsync(ctx.User.Id, ct);
            await ctx.ReplyAsync(ctx.T(cleared ? "cancel.done" : "cancel.nothing"), ct: ct);
            return;
        }

        // Any other command abandons an unfinished wizard.
        await conversation.ClearAsync(ctx.User.Id, ct);

        if (command == "/start")
        {
            // Runs without a tenant: creating a shop or joining one crosses tenants.
            ctx.Tenant.Clear();
            await start.HandleStartAsync(ctx, args, ct);
            return;
        }

        await ResolveTenantAsync(ctx, ct);
        switch (command)
        {
            case "/menu":
                await menu.ShowAsync(ctx, ct);
                break;
            case "/shops":
                await shopsHandler.ListAsync(ctx, ct);
                break;
            case "/language":
                await language.AskAsync(ctx, ct);
                break;
            case "/invite":
                await invite.CreateAsync(ctx, ct);
                break;
            case "/help":
                await ctx.ReplyAsync(ctx.T("help.text"), ct: ct);
                break;
            default:
                if (ctx.Membership is null)
                {
                    await start.BeginWizardAsync(ctx, ct);
                }
                else
                {
                    await ctx.ReplyAsync(ctx.T("help.text"), ct: ct);
                }

                break;
        }
    }

    private async Task RouteCallbackAsync(BotContext ctx, string data, CancellationToken ct)
    {
        if (data.StartsWith("w:", StringComparison.Ordinal))
        {
            ctx.Tenant.Clear();
            await start.HandleWizardCallbackAsync(ctx, data, ct);
        }
        else if (data.StartsWith(Callbacks.SwitchShop, StringComparison.Ordinal))
        {
            await shopsHandler.HandleCallbackAsync(ctx, data, ct);
        }
        else if (data.StartsWith(Callbacks.SetLang, StringComparison.Ordinal))
        {
            await language.HandleCallbackAsync(ctx, data, ct);
        }
        else if (data.StartsWith(Entries.EntryCard.Prefix, StringComparison.Ordinal))
        {
            await entries.HandleCallbackAsync(ctx, data, ct);
        }
        else
        {
            await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
        }
    }

    private async Task ResolveTenantAsync(BotContext ctx, CancellationToken ct)
    {
        ctx.Membership = await shops.ResolveActiveAsync(ctx.User.Id, ct);
        if (ctx.Membership is { } m)
        {
            ctx.Tenant.Set(m.ShopId, m.MemberId, ctx.User.Id, m.Role, ctx.User.Language);
        }
        else
        {
            ctx.Tenant.Set(null, null, ctx.User.Id, null, ctx.User.Language);
        }
    }
}
