using Debits.Application.Tenancy;
using Debits.Domain.Entities;
using Debits.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Debits.Integration.Tests.Persistence;

public class TenantIsolationTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Customers_AreInvisibleAcrossShops()
    {
        var (shopA, ownerA) = await fx.CreateShopAsync("A");
        var (shopB, _) = await fx.CreateShopAsync("B");

        using (var a = fx.Scope(shopA.Id, ownerA.Id, MemberRole.Owner))
        {
            // ShopId left empty on purpose: SaveChanges must stamp it from the tenant.
            a.Db.Customers.Add(new Customer { Name = "Ahmad aka", SearchKey = "ahmad aka" });
            await a.Db.SaveChangesAsync();
        }

        using (var b = fx.Scope(shopB.Id))
        {
            (await b.Db.Customers.ToListAsync()).Should().BeEmpty();
            (await b.Db.CustomerBalances.ToListAsync()).Should().BeEmpty();
        }

        using (var a = fx.Scope(shopA.Id))
        {
            var customer = await a.Db.Customers.SingleAsync();
            customer.ShopId.Should().Be(shopA.Id);
            customer.CreatedAt.Should().Be(fx.Clock.GetUtcNow().UtcDateTime);
        }

        using (var none = fx.Scope())
        {
            (await none.Db.Customers.CountAsync(c => c.ShopId == shopA.Id)).Should().Be(1);
        }
    }

    [Fact]
    public async Task SavingEntityOfAnotherShop_Throws()
    {
        var (shopA, _) = await fx.CreateShopAsync("A2");
        var (shopB, _) = await fx.CreateShopAsync("B2");

        using var scope = fx.Scope(shopA.Id);
        scope.Db.Customers.Add(new Customer { ShopId = shopB.Id, Name = "Intruder", SearchKey = "intruder" });

        var act = () => scope.Db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantMismatchException>();
    }

    [Fact]
    public async Task SavingTenantEntity_WithoutTenant_RequiresExplicitShopId()
    {
        using var scope = fx.Scope();
        scope.Db.Customers.Add(new Customer { Name = "Nobody", SearchKey = "nobody" });

        var act = () => scope.Db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantMismatchException>();
    }

    [Fact]
    public async Task Enums_AreStoredAsLowercaseText()
    {
        var (shop, owner) = await fx.CreateShopAsync("Enums");
        using var scope = fx.Scope(shop.Id, owner.Id, MemberRole.Owner);
        var customer = Customer.Create(shop.Id, "Karim", null, owner.Id);
        scope.Db.Customers.Add(customer);
        scope.Db.Transactions.Add(new Transaction
        {
            Customer = customer,
            Type = TransactionType.Debt,
            Amount = 1000,
            DueDate = new DateOnly(2026, 11, 1),
            Source = EntrySource.Voice,
            CreatedByMemberId = owner.Id,
        });
        await scope.Db.SaveChangesAsync();

        var raw = await scope.Db.Database
            .SqlQuery<string>($"SELECT type || '/' || source AS \"Value\" FROM transactions WHERE customer_id = {customer.Id}")
            .SingleAsync();
        raw.Should().Be("debt/voice");

        var role = await scope.Db.Database
            .SqlQuery<string>($"SELECT role AS \"Value\" FROM shop_members WHERE id = {owner.Id}")
            .SingleAsync();
        role.Should().Be("owner");
    }

    [Fact]
    public async Task DebtWithoutDueDate_IsRejectedByCheckConstraint()
    {
        var (shop, owner) = await fx.CreateShopAsync("Check");
        using var scope = fx.Scope(shop.Id, owner.Id, MemberRole.Owner);
        var customer = Customer.Create(shop.Id, "Nodir", null, owner.Id);
        scope.Db.Customers.Add(customer);
        scope.Db.Transactions.Add(new Transaction { Customer = customer, Type = TransactionType.Debt, Amount = 1000, CreatedByMemberId = owner.Id });

        var act = () => scope.Db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
