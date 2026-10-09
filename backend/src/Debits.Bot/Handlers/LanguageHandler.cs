using Debits.Application.Users;
using Debits.Bot.Dispatch;
using Debits.Domain.Entities;

namespace Debits.Bot.Handlers;

public sealed class LanguageHandler(UserService users)
{
    public Task AskAsync(BotContext ctx, CancellationToken ct) =>
        ctx.ReplyAsync(ctx.T("lang.choose"), Keyboards.Languages(ctx, Callbacks.SetLang), ct);

    /// <summary>Callback <c>lang:&lt;code&gt;</c>.</summary>
    public async Task HandleCallbackAsync(BotContext ctx, string data, CancellationToken ct)
    {
        AppLanguage language;
        try
        {
            language = AppLanguageCodes.FromCode(data[Callbacks.SetLang.Length..]);
        }
        catch (ArgumentOutOfRangeException)
        {
            await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
            return;
        }

        await users.SetLanguageAsync(ctx.User.Id, language, ct);
        ctx.User.Language = language;
        await ctx.AnswerCallbackAsync(ct: ct);
        await ctx.EditOrReplyAsync(ctx.T("lang.set", ctx.T("lang." + language.ToCode())), ct: ct);
    }
}
