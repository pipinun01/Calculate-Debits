using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Bot.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

/// <summary>
/// Excel files. <c>GET</c> returns the file itself (a browser can save it); <c>POST …/send</c> makes the bot send it
/// to the caller's chat, which is how the Mini App delivers files inside Telegram.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/export")]
public sealed class ExportController(ReportSender sender) : ControllerBase
{
    [HttpGet("customers/{id:guid}")]
    public async Task<IActionResult> Customer(Guid id, CancellationToken ct) => Download(await sender.CustomerLedgerFileAsync(id, ct));

    [HttpPost("customers/{id:guid}/send")]
    public async Task<IActionResult> SendCustomer(Guid id, CancellationToken ct)
    {
        await sender.SendCustomerLedgerAsync(id, ct);
        return NoContent();
    }

    /// <summary>The whole shop for a period: summary, all debtors, every entry. Owners only.</summary>
    [HttpGet("shop")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<IActionResult> Shop([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Download(await sender.ShopFileAsync(from, to, ct));

    [HttpPost("shop/send")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<IActionResult> SendShop([FromBody] PeriodRequest? request, CancellationToken ct)
    {
        await sender.SendShopFileAsync(request?.From, request?.To, ct);
        return NoContent();
    }

    private FileContentResult Download(ExportFile file) => File(file.Content, ExportFile.ContentType, file.FileName);
}
