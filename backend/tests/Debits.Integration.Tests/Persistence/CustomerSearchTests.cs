using Debits.Application.Customers;
using Debits.Domain.Customers;
using Debits.Domain.Entities;

namespace Debits.Integration.Tests.Persistence;

public class CustomerSearchTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> SeedAsync()
    {
        var (shop, owner) = await fx.CreateShopAsync("Search");
        using var scope = fx.Scope(shop.Id, owner.Id, MemberRole.Owner);
        scope.Db.Customers.AddRange(
            Customer.Create(shop.Id, "Ahmad aka", "stomatolog", owner.Id),
            Customer.Create(shop.Id, "Ahmad aka", "qo'shni", owner.Id),
            Customer.Create(shop.Id, "Karim", null, owner.Id),
            Customer.Create(shop.Id, "Dilnoza opa", null, owner.Id),
            Customer.Create(shop.Id, "Ўткир", null, owner.Id));
        await scope.Db.SaveChangesAsync();
        return shop.Id;
    }

    [Fact]
    public async Task Resolve_TwoSimilarNames_IsAmbiguous()
    {
        var shopId = await SeedAsync();
        using var scope = fx.Scope(shopId);

        var result = await scope.Get<CustomerService>().ResolveAsync("Ахмад ака");

        var ambiguous = result.Should().BeOfType<CustomerResolution.Ambiguous>().Subject;
        ambiguous.Candidates.Select(c => c.DisplayName).Should().BeEquivalentTo("Ahmad aka (stomatolog)", "Ahmad aka (qo'shni)");
    }

    [Fact]
    public async Task Resolve_WithTag_IsExact()
    {
        var shopId = await SeedAsync();
        using var scope = fx.Scope(shopId);

        var result = await scope.Get<CustomerService>().ResolveAsync("Ahmad aka", "stomatolog");

        result.Should().BeOfType<CustomerResolution.Exact>().Which.Customer.Tag.Should().Be("stomatolog");
    }

    [Fact]
    public async Task Resolve_Typo_StillExact()
    {
        var shopId = await SeedAsync();
        using var scope = fx.Scope(shopId);

        var result = await scope.Get<CustomerService>().ResolveAsync("Dilnaza opa");

        result.Should().BeOfType<CustomerResolution.Exact>().Which.Customer.Name.Should().Be("Dilnoza opa");
    }

    [Fact]
    public async Task Resolve_CyrillicStoredLatinQueried_IsExact()
    {
        var shopId = await SeedAsync();
        using var scope = fx.Scope(shopId);

        var result = await scope.Get<CustomerService>().ResolveAsync("O'tkir");

        result.Should().BeOfType<CustomerResolution.Exact>().Which.Customer.Name.Should().Be("Ўткир");
    }

    [Fact]
    public async Task Resolve_Unknown_IsNone()
    {
        var shopId = await SeedAsync();
        using var scope = fx.Scope(shopId);

        var result = await scope.Get<CustomerService>().ResolveAsync("Zuhra");

        result.Should().BeOfType<CustomerResolution.None>();
    }

    [Fact]
    public async Task Search_IsScopedToTenant()
    {
        await SeedAsync();
        var (other, _) = await fx.CreateShopAsync("Other");
        using var scope = fx.Scope(other.Id);

        (await scope.Get<CustomerService>().SearchAsync("Ahmad")).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_OrdersBySimilarity()
    {
        var shopId = await SeedAsync();
        using var scope = fx.Scope(shopId);

        var result = await scope.Get<CustomerService>().SearchAsync("ahmad");

        result.Should().NotBeEmpty();
        result.Should().BeInDescendingOrder(c => c.Similarity);
        result.Should().OnlyContain(c => c.Name.StartsWith("Ahmad"));
    }
}
