using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Debts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/debts")]
public sealed class DebtsController(DebtService debts) : ControllerBase
{
    /// <param name="status">open (all) | overdue | due_soon | on_time</param>
    [HttpGet]
    public async Task<ActionResult<DebtsResponse>> List([FromQuery] string? status, [FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken ct)
    {
        var page = await debts.ListAsync(QueryEnum.Parse(status, DebtStatus.Open), cursor, limit, ct);
        return new DebtsResponse(await debts.SummaryAsync(ct), page.Items, page.NextCursor);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DebtSummary>> Summary(CancellationToken ct) => await debts.SummaryAsync(ct);
}

public sealed record DebtsResponse(DebtSummary Summary, IReadOnlyList<OpenDebtDto> Items, string? NextCursor);
