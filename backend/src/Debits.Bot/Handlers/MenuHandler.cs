using Debits.Bot.Dispatch;
using Debits.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Debits.Bot.Handlers;

public sealed class MenuHandler(IOptions<MiniAppOptions> miniApp)
{
    public async Task ShowAsync(BotContext ctx, CancellationToken ct)
    {
        if (ctx.Membership is null)
        {
            await ctx.ReplyAsync(ctx.T("shops.none"), ct: ct);
            return;
        }

        var m = ctx.Membership;
        var text = ctx.T("menu.title",
            BotContext.Esc(m.ShopName),
            ctx.T("role." + m.Role.ToString().ToLowerInvariant()),
            ctx.T("lang." + ctx.Language.ToCode()));
        text += miniApp.Value.IsConfigured ? ctx.T("menu.hint") : ctx.T("menu.no_app") + ctx.T("menu.hint");

        await ctx.ReplyAsync(text, Keyboards.OpenApp(ctx, miniApp.Value.Url), ct);
    }
}
