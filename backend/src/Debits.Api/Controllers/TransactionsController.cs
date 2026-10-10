using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/transactions")]
public sealed class TransactionsController(LedgerService ledger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<LedgerMutation>> Create([FromBody] CreateTransactionRequest request, CancellationToken ct)
    {
        var result = await ledger.CreateAsync(
            new CreateTransactionCommand(request.CustomerId, request.Type, request.Amount, request.Comment, request.DueDate, request.TargetDebtId), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Transaction.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransactionDto>> Get(Guid id, CancellationToken ct) => await ledger.GetAsync(id, ct);

    /// <summary>Owner: any entry. Worker: own entries within 10 minutes of creation.</summary>
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<LedgerMutation>> Update(Guid id, [FromBody] UpdateTransactionRequest request, CancellationToken ct) =>
        await ledger.UpdateAsync(id, new UpdateTransactionCommand(request.Amount, request.Comment, request.DueDate, request.TargetDebtId), ct);

    /// <summary>Soft delete with the same permission rule as PATCH. Returns the customer's new balance.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<DeleteTransactionResponse>> Delete(Guid id, [FromQuery] string? reason, CancellationToken ct) =>
        new DeleteTransactionResponse(id, await ledger.DeleteAsync(id, reason, ct));
}

public sealed record DeleteTransactionResponse(Guid Id, long Balance);
