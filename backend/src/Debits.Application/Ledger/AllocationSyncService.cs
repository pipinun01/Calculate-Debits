using Debits.Application.Abstractions;
using Debits.Domain.Entities;
using Debits.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Ledger;

/// <summary>
/// Rebuilds <c>payment_allocations</c> for one customer from scratch after any ledger mutation.
/// Caller is expected to hold the customer row lock inside a transaction (see <see cref="IDebitsDbContext.LockCustomerAsync"/>).
/// </summary>
public sealed class AllocationSyncService(IDebitsDbContext db)
{
    public async Task<AllocationResult> RecalculateAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        // All ids incl. soft-deleted rows: their stale allocations must go too.
        var allIds = await db.Transactions
            .IgnoreQueryFilters(["SoftDelete"])
            .Where(t => t.CustomerId == customerId)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var live = await db.Transactions
            .Where(t => t.CustomerId == customerId)
            .Select(t => new LedgerEntry(t.Id, t.Type, t.Amount, t.CreatedAt, t.TargetDebtId))
            .ToListAsync(cancellationToken);

        var result = AllocationRecalculator.Recalculate(live);

        var existing = await db.PaymentAllocations
            .Where(a => allIds.Contains(a.PaymentId) || allIds.Contains(a.DebtId))
            .ToListAsync(cancellationToken);
        db.PaymentAllocations.RemoveRange(existing);

        db.PaymentAllocations.AddRange(result.Allocations.Select(a => new PaymentAllocation
        {
            PaymentId = a.PaymentId,
            DebtId = a.DebtId,
            Amount = a.Amount,
        }));

        await db.SaveChangesAsync(cancellationToken);
        return result;
    }
}
