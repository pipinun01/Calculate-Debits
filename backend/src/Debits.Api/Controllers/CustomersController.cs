using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Common;
using Debits.Application.Customers;
using Debits.Application.Ledger;
using Debits.Domain.Customers;
using Debits.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.ShopMember)]
[Route("api/customers")]
public sealed class CustomersController(CustomerDirectoryService directory, CustomerService search, LedgerService ledger) : ControllerBase
{
    /// <param name="q">Fuzzy search; when set, results come in similarity order and are not paginated.</param>
    /// <param name="filter">all | debtors | due_soon | archived</param>
    /// <param name="sort">name | balance | due</param>
    [HttpGet]
    public async Task<ActionResult<Page<CustomerListItem>>> List(
        [FromQuery] string? q,
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken ct) =>
        await directory.ListAsync(new CustomerListQuery(q, QueryEnum.Parse(filter, CustomerFilter.All), QueryEnum.Parse(sort, CustomerSort.Name), cursor, limit), ct);

    /// <summary>Autocomplete candidates with similarity, for the entry form.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<CustomerCandidate>>> Search([FromQuery] string? q, [FromQuery] int? limit, CancellationToken ct) =>
        Ok(await search.SearchAsync(q ?? string.Empty, Math.Clamp(limit ?? CustomerService.DefaultSearchLimit, 1, 50), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDetail>> Get(Guid id, CancellationToken ct) => await directory.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<CustomerDetail>> Create([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var created = await directory.CreateAsync(new CreateCustomerCommand(request.Name, request.Tag, request.Phone, request.Notes), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<CustomerDetail>> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct) =>
        await directory.UpdateAsync(id, new UpdateCustomerCommand(request.Name, request.Tag, request.Phone, request.Notes, request.IsArchived), ct);

    /// <param name="type">debt | payment (optional)</param>
    [HttpGet("{id:guid}/ledger")]
    public async Task<ActionResult<Page<TransactionDto>>> Ledger(Guid id, [FromQuery] string? cursor, [FromQuery] int? limit, [FromQuery] string? type, CancellationToken ct)
    {
        await directory.GetAsync(id, ct); // 404 for unknown / foreign customers
        TransactionType? t = string.IsNullOrWhiteSpace(type) ? null : QueryEnum.Parse(type, TransactionType.Debt);
        return await ledger.LedgerAsync(id, cursor, limit, t, ct);
    }
}
