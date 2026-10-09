using Debits.Domain.Entities;

namespace Debits.Domain.Ledger;

/// <summary>Input row for recalculation: a non-deleted transaction of one customer.</summary>
public sealed record LedgerEntry(Guid Id, TransactionType Type, long Amount, DateTime CreatedAt, Guid? TargetDebtId = null);

public sealed record Allocation(Guid PaymentId, Guid DebtId, long Amount);

public sealed record DebtState(Guid DebtId, long Amount, long Remaining)
{
    public bool IsOpen => Remaining > 0;
}

public sealed record AllocationResult(
    IReadOnlyList<Allocation> Allocations,
    IReadOnlyList<DebtState> Debts,
    long UnallocatedCredit,
    long TotalDebt,
    long TotalPaid)
{
    public long Balance => TotalDebt - TotalPaid;

    public IEnumerable<DebtState> OpenDebts => Debts.Where(d => d.IsOpen);
}

/// <summary>
/// Pure FIFO allocation of payments to debts, replayed over the customer's ledger in chronological order.
/// A payment with <c>TargetDebtId</c> covers that debt first; the remainder and all other payments cover the oldest open debts.
/// Credit left after all debts are closed is remembered and consumed by the next debt.
/// </summary>
public static class AllocationRecalculator
{
    public static AllocationResult Recalculate(IEnumerable<LedgerEntry> entries)
    {
        var ordered = entries
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();

        var debts = new List<MutableDebt>();
        var credits = new List<MutableCredit>();
        var allocations = new Dictionary<(Guid Payment, Guid Debt), long>();
        long totalDebt = 0;
        long totalPaid = 0;

        void Allocate(Guid paymentId, MutableDebt debt, long amount)
        {
            if (amount <= 0)
            {
                return;
            }

            debt.Remaining -= amount;
            var key = (paymentId, debt.Id);
            allocations[key] = allocations.GetValueOrDefault(key) + amount;
        }

        foreach (var entry in ordered)
        {
            if (entry.Amount <= 0)
            {
                throw new DomainException("ledger.invalid_amount", $"Transaction {entry.Id} has non-positive amount {entry.Amount}");
            }

            if (entry.Type == TransactionType.Debt)
            {
                totalDebt += entry.Amount;
                var debt = new MutableDebt(entry.Id, entry.Amount);
                foreach (var credit in credits)
                {
                    if (debt.Remaining == 0)
                    {
                        break;
                    }

                    var take = Math.Min(credit.Remaining, debt.Remaining);
                    credit.Remaining -= take;
                    Allocate(credit.PaymentId, debt, take);
                }

                credits.RemoveAll(c => c.Remaining == 0);
                debts.Add(debt);
                continue;
            }

            totalPaid += entry.Amount;
            var rest = entry.Amount;

            if (entry.TargetDebtId is { } targetId)
            {
                var target = debts.FirstOrDefault(d => d.Id == targetId && d.Remaining > 0);
                if (target is not null)
                {
                    var take = Math.Min(rest, target.Remaining);
                    Allocate(entry.Id, target, take);
                    rest -= take;
                }
            }

            foreach (var debt in debts)
            {
                if (rest == 0)
                {
                    break;
                }

                if (debt.Remaining == 0)
                {
                    continue;
                }

                var take = Math.Min(rest, debt.Remaining);
                Allocate(entry.Id, debt, take);
                rest -= take;
            }

            if (rest > 0)
            {
                credits.Add(new MutableCredit(entry.Id, rest));
            }
        }

        return new AllocationResult(
            allocations.Select(kv => new Allocation(kv.Key.Payment, kv.Key.Debt, kv.Value)).ToList(),
            debts.Select(d => new DebtState(d.Id, d.Amount, d.Remaining)).ToList(),
            credits.Sum(c => c.Remaining),
            totalDebt,
            totalPaid);
    }

    private sealed class MutableDebt(Guid id, long amount)
    {
        public Guid Id { get; } = id;
        public long Amount { get; } = amount;
        public long Remaining { get; set; } = amount;
    }

    private sealed class MutableCredit(Guid paymentId, long remaining)
    {
        public Guid PaymentId { get; } = paymentId;
        public long Remaining { get; set; } = remaining;
    }
}
