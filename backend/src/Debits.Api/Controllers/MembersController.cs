using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Members;
using Debits.Application.Tenancy;
using Debits.Bot;
using Debits.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/members")]
public sealed class MembersController(MembershipService members, ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemberResponse>>> List(CancellationToken ct) =>
        Ok((await members.ListAsync(ct)).Select(m => MemberResponse.From(m, tenant.MemberId)).ToList());

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<ActionResult<MemberResponse>> Update(Guid id, [FromBody] UpdateMemberRequest request, CancellationToken ct) =>
        MemberResponse.From(await members.ChangeRoleAsync(id, request.Role, ct), tenant.MemberId);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await members.DeactivateAsync(id, ct);
        return NoContent();
    }

    /// <summary>The caller leaves the current shop. The last owner cannot (409 <c>member.last_owner</c>).</summary>
    [HttpPost("leave")]
    public async Task<IActionResult> Leave(CancellationToken ct)
    {
        await members.DeactivateAsync(tenant.MemberId!.Value, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize(Policy = Policies.OwnerOnly)]
[Route("api/invites")]
public sealed class InvitesController(MembershipService members, ITenantContext tenant, BotIdentity bot) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InviteResponse>>> List(CancellationToken ct) =>
        Ok((await members.ListActiveInviteDtosAsync(tenant.ShopId!.Value, ct)).Select(ToResponse).ToList());

    [HttpPost]
    public async Task<ActionResult<InviteResponse>> Create([FromBody] CreateInviteRequest? request, CancellationToken ct)
    {
        var invite = await members.CreateInviteAsync(tenant.ShopId!.Value, tenant.MemberId!.Value, request?.Role ?? MemberRole.Worker, ct);
        var dto = (await members.ListActiveInviteDtosAsync(tenant.ShopId!.Value, ct)).First(i => i.Id == invite.Id);
        return Created($"/api/invites/{invite.Id}", ToResponse(dto));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await members.RevokeInviteAsync(id, ct);
        return NoContent();
    }

    private InviteResponse ToResponse(InviteDto i) =>
        new(i.Id, i.Role, i.ExpiresAt, i.CreatedAt, i.CreatedByName, bot.DeepLink(MembershipService.InvitePayload(i.Token)));
}
