using Debits.Application.Shops;
using Debits.Bot.Dispatch;
using Debits.Domain;

namespace Debits.Bot.Handlers;

public sealed class ShopsHandler(ShopService shops, StartHandler start, MenuHandler menu)
{
    public async Task ListAsync(BotContext ctx, CancellationToken ct)
    {
        var list = await shops.ListForUserAsync(ctx.User.Id, ct);
        if (list.Count == 0)
        {
            await ctx.ReplyAsync(ctx.T("shops.none"), ct: ct);
            return;
        }

        await ctx.ReplyAsync(ctx.T("shops.title"), Keyboards.Shops(ctx, list), ct);
    }

    /// <summary>Callback <c>shop:new</c> / <c>shop:&lt;id&gt;</c>.</summary>
    public async Task HandleCallbackAsync(BotContext ctx, string data, CancellationToken ct)
    {
        if (data == Callbacks.NewShop)
        {
            await ctx.AnswerCallbackAsync(ct: ct);
            await start.BeginWizardAsync(ctx, ct);
            return;
        }

        if (!Guid.TryParseExact(data[Callbacks.SwitchShop.Length..], "N", out var shopId))
        {
            await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
            return;
        }

        try
        {
            ctx.Membership = await shops.SetActiveAsync(ctx.User.Id, shopId, ct);
        }
        catch (DomainException)
        {
            await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
            return;
        }

        await ctx.AnswerCallbackAsync(ct: ct);
        await ctx.EditOrReplyAsync(ctx.T("shops.switched", BotContext.Esc(ctx.Membership.ShopName)), ct: ct);
        await menu.ShowAsync(ctx, ct);
    }
}
