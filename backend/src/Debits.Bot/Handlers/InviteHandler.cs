using Debits.Application.Members;
using Debits.Bot.Dispatch;
using Debits.Domain.Entities;

namespace Debits.Bot.Handlers;

public sealed class InviteHandler(MembershipService members, BotIdentity identity)
{
    public async Task CreateAsync(BotContext ctx, CancellationToken ct)
    {
        if (ctx.Membership is null)
        {
            await ctx.ReplyAsync(ctx.T("shops.none"), ct: ct);
            return;
        }

        if (ctx.Membership.Role != MemberRole.Owner)
        {
            await ctx.ReplyAsync(ctx.T("invite.owner_only"), ct: ct);
            return;
        }

        var invite = await members.CreateInviteAsync(ctx.Membership.ShopId, ctx.Membership.MemberId, MemberRole.Worker, ct);
        var link = identity.DeepLink(MembershipService.InvitePayload(invite.Token));
        var shopName = BotContext.Esc(ctx.Membership.ShopName);

        await ctx.ReplyAsync(
            ctx.T("invite.created", shopName, link),
            Keyboards.Share(ctx.T("invite.share"), link, ctx.T("invite.share_text", ctx.Membership.ShopName, link)),
            ct);
    }
}
