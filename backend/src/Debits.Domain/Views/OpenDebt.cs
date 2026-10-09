using Debits.Domain.Entities;

namespace Debits.Domain.Views;

/// <summary>Keyless view <c>open_debts</c>: debts whose remaining = amount − Σ allocations is still positive.</summary>
public sealed class OpenDebt : ITenantEntity
{
    public Guid ShopId { get; set; }
    public Guid DebtId { get; set; }
    public Guid CustomerId { get; set; }
    public long Amount { get; set; }
    public long Remaining { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByMemberId { get; set; }
}
