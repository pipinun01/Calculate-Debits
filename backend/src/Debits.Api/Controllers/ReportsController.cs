using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Reports;
using Debits.Bot.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/reports")]
public sealed class ReportsController(ReportService reports, ReportSender sender) : ControllerBase
{
    /// <summary>Inclusive local date range; defaults to the current month in the shop's time zone.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummary>> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        await reports.SummaryAsync(from, to, ct);

    [HttpGet("top-debtors")]
    public async Task<ActionResult<IReadOnlyList<TopDebtor>>> TopDebtors([FromQuery] int? limit, CancellationToken ct) =>
        Ok(await reports.TopDebtorsAsync(limit ?? ReportService.TopDebtorsDefault, ct));

    [HttpGet("collections")]
    public async Task<ActionResult<IReadOnlyList<CollectionPoint>>> Collections([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await reports.CollectionsAsync(from, to, ct));

    /// <summary>The bot sends the period summary as a message to the caller's chat.</summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] PeriodRequest? request, CancellationToken ct)
    {
        await sender.SendSummaryAsync(request?.From, request?.To, ct);
        return NoContent();
    }
}
