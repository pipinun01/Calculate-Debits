using System.Net;
using Debits.Api.Contracts;
using Debits.Api.Controllers;
using Debits.Application.Customers;
using Debits.Application.Ledger;
using Debits.Domain.Entities;

namespace Debits.Integration.Tests.Api;

/// <summary>Owner × worker over every shop endpoint: the owner may do everything, the worker everything except settings, staff, invites, audit and the shop export.</summary>
public class RoleMatrixTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private sealed record Case(string Name, Func<HttpClient, Task<HttpResponseMessage>> Call, bool Worker);

    [Fact]
    public async Task EveryEndpoint_OwnerAllowed_WorkerOnlyWhereTheRoleAllows()
    {
        var s = await fx.ShopWithWorkerAsync("Matritsa");
        var owner = s.Owner.In(s.ShopId);
        var worker = s.Worker.In(s.ShopId);

        var customer = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Ahmad aka", null, null, null))).ReadAsync<CustomerDetail>();
        var debt = await (await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 100_000, null, null, null))).ReadAsync<LedgerMutation>();
        var members = await (await owner.GetAsync("/api/members")).ReadAsync<List<MemberResponse>>();
        var workerMember = members.Single(m => m.Role == MemberRole.Worker);
        var invite = await (await owner.PostJsonAsync("/api/invites", new CreateInviteRequest(null))).ReadAsync<InviteResponse>();
        var n = 0;

        var cases = new List<Case>
        {
            new("GET /api/me", c => c.GetAsync("/api/me"), true),
            new("GET /api/shops", c => c.GetAsync("/api/shops"), true),
            new("GET /api/shop", c => c.GetAsync("/api/shop"), true),
            new("GET /api/members", c => c.GetAsync("/api/members"), true),
            new("GET /api/customers", c => c.GetAsync("/api/customers"), true),
            new("GET /api/customers/search", c => c.GetAsync("/api/customers/search?q=ahmad"), true),
            new("GET /api/customers/{id}", c => c.GetAsync($"/api/customers/{customer.Id}"), true),
            new("GET /api/customers/{id}/ledger", c => c.GetAsync($"/api/customers/{customer.Id}/ledger"), true),
            new("POST /api/customers", c => c.PostJsonAsync("/api/customers", new CreateCustomerRequest($"Mijoz {++n}", null, null, null)), true),
            new("PATCH /api/customers/{id}", c => c.PatchJsonAsync($"/api/customers/{customer.Id}", new UpdateCustomerRequest(null, null, null, "eslatma", null)), true),
            new("POST /api/transactions", c => c.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Payment, 1_000, null, null, null)), true),
            new("GET /api/transactions/{id}", c => c.GetAsync($"/api/transactions/{debt.Transaction.Id}"), true),
            new("GET /api/debts", c => c.GetAsync("/api/debts"), true),
            new("GET /api/debts/summary", c => c.GetAsync("/api/debts/summary"), true),
            new("GET /api/reports/summary", c => c.GetAsync("/api/reports/summary"), true),
            new("GET /api/reports/top-debtors", c => c.GetAsync("/api/reports/top-debtors"), true),
            new("GET /api/reports/collections", c => c.GetAsync("/api/reports/collections"), true),
            new("POST /api/reports/send", c => c.PostJsonAsync("/api/reports/send", new PeriodRequest(null, null)), true),
            new("POST /api/entries/parse", c => c.PostJsonAsync("/api/entries/parse", new ParseEntryRequest("Ahmad aka 50 ming qarz")), true),
            new("GET /api/export/customers/{id}", c => c.GetAsync($"/api/export/customers/{customer.Id}"), true),
            new("POST /api/export/customers/{id}/send", c => c.PostAsync($"/api/export/customers/{customer.Id}/send", null), true),

            new("PATCH /api/shop", c => c.PatchJsonAsync("/api/shop", new UpdateShopRequest(null, 45, null, null, null, null)), false),
            new("GET /api/audit", c => c.GetAsync("/api/audit"), false),
            new("GET /api/export/shop", c => c.GetAsync("/api/export/shop"), false),
            new("POST /api/export/shop/send", c => c.PostJsonAsync("/api/export/shop/send", new PeriodRequest(null, null)), false),
            new("GET /api/invites", c => c.GetAsync("/api/invites"), false),
            new("POST /api/invites", c => c.PostJsonAsync("/api/invites", new CreateInviteRequest(null)), false),
            new("DELETE /api/invites/{id}", c => c.DeleteAsync($"/api/invites/{invite.Id}"), false),
            new("PATCH /api/members/{id}", c => c.PatchJsonAsync($"/api/members/{workerMember.Id}", new UpdateMemberRequest(MemberRole.Worker)), false),
            // Last: it removes the worker, so the worker pass has to run before the owner pass.
            new("DELETE /api/members/{id}", c => c.DeleteAsync($"/api/members/{workerMember.Id}"), false),
        };

        foreach (var (role, client) in new[] { ("worker", worker), ("owner", owner) })
        {
            foreach (var c in cases)
            {
                var response = await c.Call(client);
                if (role == "owner" || c.Worker)
                {
                    response.IsSuccessStatusCode.Should().BeTrue($"{role} may {c.Name}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                }
                else
                {
                    response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} must not {c.Name}");
                    (await response.CodeAsync()).Should().Be("forbidden.owner_only", c.Name);
                }
            }
        }
    }

    [Fact]
    public async Task ForeignEntries_OwnerMayChange_WorkerMayNot()
    {
        var s = await fx.ShopWithWorkerAsync("Matritsa 2");
        var owner = s.Owner.In(s.ShopId);
        var worker = s.Worker.In(s.ShopId);
        var customer = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Botir", null, null, null))).ReadAsync<CustomerDetail>();
        var byOwner = await (await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 100_000, null, null, null))).ReadAsync<LedgerMutation>();
        var byWorker = await (await worker.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 70_000, null, null, null))).ReadAsync<LedgerMutation>();

        foreach (var response in new[]
        {
            await worker.PatchJsonAsync($"/api/transactions/{byOwner.Transaction.Id}", new UpdateTransactionRequest(1, null, null, null)),
            await worker.DeleteAsync($"/api/transactions/{byOwner.Transaction.Id}"),
        })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.CodeAsync()).Should().Be("transaction.forbidden");
        }

        (await owner.PatchJsonAsync($"/api/transactions/{byWorker.Transaction.Id}", new UpdateTransactionRequest(60_000, null, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/transactions/{byWorker.Transaction.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/shop")]
    [InlineData("/api/customers")]
    [InlineData("/api/debts")]
    [InlineData("/api/reports/summary")]
    [InlineData("/api/audit")]
    [InlineData("/api/export/shop")]
    public async Task Stranger_IsNotLetIntoAShop(string url)
    {
        var s = await fx.ShopWithWorkerAsync("Matritsa 3 " + url);
        var stranger = await fx.LoginAsync("Begona");

        var response = await stranger.In(s.ShopId).GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.CodeAsync()).Should().Be("shop.not_member");

        using var anonymous = fx.Factory.CreateClient();
        (await anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
