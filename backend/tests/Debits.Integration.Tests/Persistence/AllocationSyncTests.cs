using Debits.Application.Ledger;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Integration.Tests.Persistence;

public class AllocationSyncTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SoftDeletingPayment_ReopensDebt_AfterRecalculation()
    {
        var (shop, owner) = await fx.CreateShopAsync("Ledger");
        using var scope = fx.Scope(shop.Id, owner.Id, MemberRole.Owner);
        var db = scope.Db;
        var now = fx.Clock.GetUtcNow().UtcDateTime;

        var customer = Customer.Create(shop.Id, "Ahmad aka", null, owner.Id);
        var debt = new Transaction
        {
            Customer = customer, Type = TransactionType.Debt, Amount = 100_000,
            DueDate = new DateOnly(2026, 11, 8), CreatedByMemberId = owner.Id, CreatedAt = now,
        };
        var payment = new Transaction
        {
            Customer = customer, Type = TransactionType.Payment, Amount = 60_000,
            CreatedByMemberId = owner.Id, CreatedAt = now.AddMinutes(5),
        };
        db.Customers.Add(customer);
        db.Transactions.AddRange(debt, payment);
        await db.SaveChangesAsync();

        var sync = scope.Get<AllocationSyncService>();
        var first = await db.InTransactionAsync(async ct =>
        {
            await db.LockCustomerAsync(customer.Id, ct);
            return await sync.RecalculateAsync(customer.Id, ct);
        });

        first.Allocations.Should().ContainSingle().Which.Amount.Should().Be(60_000);
        (await db.PaymentAllocations.CountAsync(a => a.DebtId == debt.Id)).Should().Be(1);

        var open = await db.OpenDebts.SingleAsync(d => d.CustomerId == customer.Id);
        open.Remaining.Should().Be(40_000);
        (await db.CustomerBalances.SingleAsync(b => b.CustomerId == customer.Id)).Balance.Should().Be(40_000);

        payment.SoftDelete(owner.Id, now.AddMinutes(6), "mistake");
        await db.SaveChangesAsync();

        var second = await sync.RecalculateAsync(customer.Id);

        second.Allocations.Should().BeEmpty();
        (await db.PaymentAllocations.CountAsync(a => a.DebtId == debt.Id)).Should().Be(0);
        (await db.OpenDebts.SingleAsync(d => d.CustomerId == customer.Id)).Remaining.Should().Be(100_000);
        (await db.CustomerBalances.SingleAsync(b => b.CustomerId == customer.Id)).Balance.Should().Be(100_000);

        // soft-deleted row is hidden by the named filter, visible when it is ignored
        (await db.Transactions.CountAsync(t => t.CustomerId == customer.Id)).Should().Be(1);
        (await db.Transactions.IgnoreQueryFilters(["SoftDelete"]).CountAsync(t => t.CustomerId == customer.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Overpayment_ClosesDebt_AndNextDebtConsumesCredit()
    {
        var (shop, owner) = await fx.CreateShopAsync("Ledger2");
        using var scope = fx.Scope(shop.Id, owner.Id, MemberRole.Owner);
        var db = scope.Db;
        var now = fx.Clock.GetUtcNow().UtcDateTime;

        var customer = Customer.Create(shop.Id, "Karim", null, owner.Id);
        db.Customers.Add(customer);
        db.Transactions.AddRange(
            new Transaction { Customer = customer, Type = TransactionType.Debt, Amount = 100_000, DueDate = new DateOnly(2026, 11, 8), CreatedByMemberId = owner.Id, CreatedAt = now },
            new Transaction { Customer = customer, Type = TransactionType.Payment, Amount = 150_000, CreatedByMemberId = owner.Id, CreatedAt = now.AddMinutes(1) });
        await db.SaveChangesAsync();

        var sync = scope.Get<AllocationSyncService>();
        var r1 = await sync.RecalculateAsync(customer.Id);
        r1.UnallocatedCredit.Should().Be(50_000);
        (await db.OpenDebts.AnyAsync(d => d.CustomerId == customer.Id)).Should().BeFalse();
        (await db.CustomerBalances.SingleAsync(b => b.CustomerId == customer.Id)).Balance.Should().Be(-50_000);

        db.Transactions.Add(new Transaction { Customer = customer, Type = TransactionType.Debt, Amount = 80_000, DueDate = new DateOnly(2026, 11, 20), CreatedByMemberId = owner.Id, CreatedAt = now.AddMinutes(2) });
        await db.SaveChangesAsync();

        var r2 = await sync.RecalculateAsync(customer.Id);
        r2.UnallocatedCredit.Should().Be(0);
        (await db.OpenDebts.SingleAsync(d => d.CustomerId == customer.Id)).Remaining.Should().Be(30_000);
    }
}
