using Debits.Api.Auth;
using Debits.Application.Audit;
using Debits.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.OwnerOnly)]
[Route("api/audit")]
public sealed class AuditController(AuditService audit) : ControllerBase
{
    /// <summary>Who did what in the shop, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<Page<AuditEventDto>>> List([FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken ct) =>
        await audit.ListAsync(cursor, limit, ct);
}
