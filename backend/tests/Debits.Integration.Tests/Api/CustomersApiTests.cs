using System.Net;
using Debits.Api.Contracts;
using Debits.Application.Common;
using Debits.Application.Customers;
using Debits.Application.Ledger;
using Debits.Domain.Customers;
using Debits.Domain.Entities;

namespace Debits.Integration.Tests.Api;

public class CustomersApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Create_Get_Update_Archive()
    {
        var s = await fx.ShopWithWorkerAsync("Customers A");
        var owner = s.Owner.In(s.ShopId);

        var created = await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("  Ahmad aka ", "stomatolog", "+998901234567", "do'kon yonida"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await created.ReadAsync<CustomerDetail>();
        customer.Name.Should().Be("Ahmad aka");
        customer.Tag.Should().Be("stomatolog");
        customer.Phone.Should().Be("+998901234567");
        customer.Balance.Should().Be(0);
        customer.CreatedByName.Should().Be("Sarvar");

        var duplicate = await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Ахмад ака", "stomatolog", null, null));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.CodeAsync()).Should().Be("customer.exists");

        var sameNameOtherTag = await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Ahmad aka", "qassob", null, null));
        sameNameOtherTag.StatusCode.Should().Be(HttpStatusCode.Created);

        var updated = await (await owner.PatchJsonAsync($"/api/customers/{customer.Id}", new UpdateCustomerRequest(null, null, "", "notes", null))).ReadAsync<CustomerDetail>();
        updated.Phone.Should().BeNull();
        updated.Notes.Should().Be("notes");
        updated.Tag.Should().Be("stomatolog");

        var archived = await (await owner.PatchJsonAsync($"/api/customers/{customer.Id}", new UpdateCustomerRequest(null, null, null, null, true))).ReadAsync<CustomerDetail>();
        archived.IsArchived.Should().BeTrue();
        var list = await (await owner.GetAsync("/api/customers")).ReadAsync<Page<CustomerListItem>>();
        list.Items.Should().NotContain(c => c.Id == customer.Id);
        var archivedList = await (await owner.GetAsync("/api/customers?filter=archived")).ReadAsync<Page<CustomerListItem>>();
        archivedList.Items.Should().ContainSingle(c => c.Id == customer.Id);

        var empty = await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("   ", null, null, null));
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.CodeAsync()).Should().Be("validation");
    }

    [Fact]
    public async Task List_Filters_Sorts_AndPaginates()
    {
        var s = await fx.ShopWithWorkerAsync("Customers B");
        var owner = s.Owner.In(s.ShopId);
        var today = fx.Today;

        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var c = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest($"Mijoz {i:00}", null, null, null))).ReadAsync<CustomerDetail>();
            ids.Add(c.Id);
        }

        // 00: overdue, 01: due in 2 days, 02: due in 20 days + payment on top, 03: paid off
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(ids[0], TransactionType.Debt, 300_000, null, today.AddDays(-5), null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(ids[1], TransactionType.Debt, 200_000, null, today.AddDays(2), null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(ids[2], TransactionType.Debt, 500_000, null, today.AddDays(20), null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(ids[2], TransactionType.Payment, 100_000, null, null, null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(ids[3], TransactionType.Debt, 50_000, null, today.AddDays(10), null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(ids[3], TransactionType.Payment, 50_000, null, null, null));

        var page1 = await (await owner.GetAsync("/api/customers?limit=5")).ReadAsync<Page<CustomerListItem>>();
        page1.Items.Should().HaveCount(5);
        page1.Items.Select(c => c.Name).Should().BeInAscendingOrder();
        page1.NextCursor.Should().NotBeNull();
        var page2 = await (await owner.GetAsync($"/api/customers?limit=5&cursor={page1.NextCursor}")).ReadAsync<Page<CustomerListItem>>();
        var page3 = await (await owner.GetAsync($"/api/customers?limit=5&cursor={page2.NextCursor}")).ReadAsync<Page<CustomerListItem>>();
        page3.Items.Should().HaveCount(2);
        page3.NextCursor.Should().BeNull();
        page1.Items.Concat(page2.Items).Concat(page3.Items).Select(c => c.Id).Should().OnlyHaveUniqueItems().And.HaveCount(12);

        var debtors = await (await owner.GetAsync("/api/customers?filter=debtors")).ReadAsync<Page<CustomerListItem>>();
        debtors.Items.Select(c => c.Id).Should().BeEquivalentTo([ids[0], ids[1], ids[2]]);

        var dueSoon = await (await owner.GetAsync("/api/customers?filter=due_soon")).ReadAsync<Page<CustomerListItem>>();
        dueSoon.Items.Select(c => c.Id).Should().BeEquivalentTo([ids[0], ids[1]]);
        dueSoon.Items.Single(c => c.Id == ids[0]).OverdueAmount.Should().Be(300_000);
        dueSoon.Items.Single(c => c.Id == ids[1]).DueSoonAmount.Should().Be(200_000);

        var byBalance = await (await owner.GetAsync("/api/customers?sort=balance&filter=debtors")).ReadAsync<Page<CustomerListItem>>();
        byBalance.Items.Select(c => c.Balance).Should().BeEquivalentTo([400_000L, 300_000L, 200_000L], o => o.WithStrictOrdering());

        var byDue = await (await owner.GetAsync("/api/customers?sort=due")).ReadAsync<Page<CustomerListItem>>();
        byDue.Items.Take(3).Select(c => c.Id).Should().ContainInOrder(ids[0], ids[1], ids[2]);
        byDue.Items.Last().NextDueDate.Should().BeNull();

        var searched = await (await owner.GetAsync("/api/customers?q=mijoz%2005")).ReadAsync<Page<CustomerListItem>>();
        searched.Items.Should().NotBeEmpty();
        searched.Items[0].Name.Should().Be("Mijoz 05");
        searched.NextCursor.Should().BeNull();

        var badSort = await owner.GetAsync("/api/customers?sort=nope");
        badSort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badSort.CodeAsync()).Should().Be("query.invalid_enum");
    }

    [Fact]
    public async Task Search_ReturnsCandidates_WithSimilarity()
    {
        var s = await fx.ShopWithWorkerAsync("Customers C");
        var owner = s.Owner.In(s.ShopId);
        await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Ahmad aka", "stomatolog", null, null));
        await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Ahmad aka", "qassob", null, null));
        await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Zulfiya opa", null, null, null));

        var hits = await (await owner.GetAsync("/api/customers/search?q=Ахмад+ака")).ReadAsync<List<CustomerCandidate>>();
        hits.Should().HaveCount(2);
        hits.Should().OnlyContain(h => h.Name == "Ahmad aka");

        var none = await (await owner.GetAsync("/api/customers/search?q=")).ReadAsync<List<CustomerCandidate>>();
        none.Should().BeEmpty();
    }

    [Fact]
    public async Task Customers_AreTenantScoped()
    {
        var a = await fx.ShopWithWorkerAsync("Customers D1");
        var b = await fx.ShopWithWorkerAsync("Customers D2");
        var c = await (await a.Owner.In(a.ShopId).PostJsonAsync("/api/customers", new CreateCustomerRequest("Only A", null, null, null))).ReadAsync<CustomerDetail>();

        (await b.Owner.In(b.ShopId).GetAsync($"/api/customers/{c.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Owner.In(b.ShopId).PatchJsonAsync($"/api/customers/{c.Id}", new UpdateCustomerRequest("Hacked", null, null, null, null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Owner.In(b.ShopId).GetAsync($"/api/customers/{c.Id}/ledger")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var listB = await (await b.Owner.In(b.ShopId).GetAsync("/api/customers")).ReadAsync<Page<CustomerListItem>>();
        listB.Items.Should().BeEmpty();

        // the same name may exist in both shops
        (await b.Owner.In(b.ShopId).PostJsonAsync("/api/customers", new CreateCustomerRequest("Only A", null, null, null))).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
