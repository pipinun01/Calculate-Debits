using Debits.Domain.Entities;

namespace Debits.Domain.Views;

/// <summary>Keyless view <c>customer_balances</c>: Σ debts − Σ payments per customer (soft-deleted rows excluded).</summary>
public sealed class CustomerBalance : ITenantEntity
{
    public Guid ShopId { get; set; }
    public Guid CustomerId { get; set; }
    public long Balance { get; set; }
}
