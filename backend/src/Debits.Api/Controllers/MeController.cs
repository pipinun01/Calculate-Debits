using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Abstractions;
using Debits.Application.Shops;
using Debits.Application.Tenancy;
using Debits.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Debits.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MeController(IDebitsDbContext db, ITenantContext tenant, ShopService shops, UserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeDto>> Get(CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == tenant.UserId, ct);
        return MeDto.From(user, await shops.ListForUserAsync(user.Id, ct));
    }

    [HttpPatch]
    public async Task<ActionResult<MeDto>> Update([FromBody] UpdateMeRequest request, CancellationToken ct)
    {
        var userId = tenant.UserId!.Value;
        if (request.Language is { } language)
        {
            await users.SetLanguageAsync(userId, language, ct);
        }

        if (request.ActiveShopId is { } shopId)
        {
            await shops.SetActiveAsync(userId, shopId, ct);
        }

        return await Get(ct);
    }
}
