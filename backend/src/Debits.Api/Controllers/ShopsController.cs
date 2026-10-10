using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Shops;
using Debits.Application.Tenancy;
using Debits.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

/// <summary>Cross-tenant: the user's shops. Needs a JWT but no <c>X-Shop-Id</c>.</summary>
[ApiController]
[Authorize]
[Route("api/shops")]
public sealed class ShopsController(ShopService shops, ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShopMembershipInfo>>> List(CancellationToken ct) =>
        Ok(await shops.ListForUserAsync(tenant.UserId!.Value, ct));

    [HttpPost]
    public async Task<ActionResult<CreateShopResponse>> Create([FromBody] CreateShopRequest request, CancellationToken ct)
    {
        var (shop, owner) = await shops.CreateAsync(
            tenant.UserId!.Value,
            request.Name,
            request.DefaultDueDays ?? Shop.DefaultDueDaysDefault,
            request.Language ?? tenant.Language,
            ct);
        var dto = await shops.GetAsync(shop.Id, ct);
        return CreatedAtAction(nameof(ShopController.Get), "Shop", null,
            new CreateShopResponse(dto, new ShopMembershipInfo(shop.Id, shop.Name, owner.Id, owner.Role, true)));
    }
}

/// <summary>The current shop (from <c>X-Shop-Id</c>).</summary>
[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/shop")]
public sealed class ShopController(ShopService shops, ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ShopDto>> Get(CancellationToken ct) => await shops.GetAsync(tenant.ShopId!.Value, ct);

    [HttpPatch]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<ActionResult<ShopDto>> Update([FromBody] UpdateShopRequest request, CancellationToken ct) =>
        await shops.UpdateAsync(tenant.ShopId!.Value,
            new UpdateShopCommand(request.Name, request.DefaultDueDays, request.Language, request.Timezone, request.ReminderRecipients, request.ReminderHour), ct);
}
