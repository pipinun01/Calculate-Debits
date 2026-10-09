namespace Debits.Domain.Entities;

/// <summary>Derived table: how much of a payment covers which debt. Fully rebuilt per customer by <c>AllocationRecalculator</c>.</summary>
public sealed class PaymentAllocation
{
    public long Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid DebtId { get; set; }
    public long Amount { get; set; }
}
