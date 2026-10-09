using Debits.Domain.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class CustomerBalanceConfiguration : IEntityTypeConfiguration<CustomerBalance>
{
    public void Configure(EntityTypeBuilder<CustomerBalance> b)
    {
        b.HasNoKey();
        b.ToView(Views.CustomerBalances);
    }
}

public sealed class OpenDebtConfiguration : IEntityTypeConfiguration<OpenDebt>
{
    public void Configure(EntityTypeBuilder<OpenDebt> b)
    {
        b.HasNoKey();
        b.ToView(Views.OpenDebts);
    }
}

/// <summary>SQL for the keyless views; applied by migrations (<c>Views.CreateSql</c> / <c>Views.DropSql</c>).</summary>
public static class Views
{
    public const string CustomerBalances = "customer_balances";
    public const string OpenDebts = "open_debts";

    public const string CreateSql = $"""
        CREATE OR REPLACE VIEW {CustomerBalances} AS
        SELECT c.shop_id,
               c.id AS customer_id,
               COALESCE(SUM(CASE WHEN t.type = 'debt' THEN t.amount ELSE -t.amount END), 0)::bigint AS balance
        FROM customers c
        LEFT JOIN transactions t ON t.customer_id = c.id AND NOT t.is_deleted
        GROUP BY c.shop_id, c.id;

        CREATE OR REPLACE VIEW {OpenDebts} AS
        SELECT d.shop_id,
               d.id AS debt_id,
               d.customer_id,
               d.amount,
               (d.amount - COALESCE(a.allocated, 0))::bigint AS remaining,
               d.due_date,
               d.created_at,
               d.created_by_member_id
        FROM transactions d
        LEFT JOIN LATERAL (
            SELECT SUM(pa.amount) AS allocated
            FROM payment_allocations pa
            JOIN transactions p ON p.id = pa.payment_id AND NOT p.is_deleted
            WHERE pa.debt_id = d.id
        ) a ON TRUE
        WHERE d.type = 'debt'
          AND NOT d.is_deleted
          AND d.amount - COALESCE(a.allocated, 0) > 0;
        """;

    public const string DropSql = $"""
        DROP VIEW IF EXISTS {OpenDebts};
        DROP VIEW IF EXISTS {CustomerBalances};
        """;
}
