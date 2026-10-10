using System.Net;
using Debits.Api.Contracts;
using Debits.Api.Controllers;
using Debits.Application.Common;
using Debits.Application.Customers;
using Debits.Application.Ledger;
using Debits.Domain.Entities;
using Debits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Debits.Integration.Tests.Api;

public class LedgerApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static async Task<CustomerDetail> CustomerAsync(HttpClient client, string name, string? tag = null) =>
        await (await client.PostJsonAsync("/api/customers", new CreateCustomerRequest(name, tag, null, null))).ReadAsync<CustomerDetail>();

    private static async Task<LedgerMutation> DebtAsync(HttpClient client, Guid customerId, long amount, DateOnly? due = null, string? comment = null) =>
        await (await client.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customerId, TransactionType.Debt, amount, comment, due, null))).ReadAsync<LedgerMutation>();

    private static async Task<LedgerMutation> PaymentAsync(HttpClient client, Guid customerId, long amount, Guid? target = null) =>
        await (await client.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customerId, TransactionType.Payment, amount, null, null, target))).ReadAsync<LedgerMutation>();

    [Fact]
    public async Task DebtThenPayment_UpdatesBalance_AndRemaining()
    {
        var s = await fx.ShopWithWorkerAsync("Ledger A");
        var owner = s.Owner.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Ahmad aka");

        var debt = await DebtAsync(owner, customer.Id, 550_000, comment: "un, shakar");
        debt.Balance.Should().Be(550_000);
        debt.Transaction.Remaining.Should().Be(550_000);
        debt.Transaction.DueDate.Should().Be(fx.Today.AddDays(30), "default due days of the shop apply");
        debt.Transaction.CreatedByName.Should().Be("Sarvar");
        debt.Transaction.CanEdit.Should().BeTrue();

        var payment = await PaymentAsync(owner, customer.Id, 200_000);
        payment.Balance.Should().Be(350_000);
        payment.Transaction.Remaining.Should().BeNull();

        var detail = await (await owner.GetAsync($"/api/customers/{customer.Id}")).ReadAsync<CustomerDetail>();
        detail.Balance.Should().Be(350_000);
        detail.OpenDebts.Should().ContainSingle().Which.Remaining.Should().Be(350_000);
        detail.LastDebt!.Id.Should().Be(debt.Transaction.Id);
        detail.LastPayment!.Id.Should().Be(payment.Transaction.Id);
        detail.NextDueDate.Should().Be(debt.Transaction.DueDate);
    }

    [Fact]
    public async Task Worker_CannotDeleteOthersEntry_ButCanDeleteOwnWithinTenMinutes()
    {
        var s = await fx.ShopWithWorkerAsync("Ledger B");
        var owner = s.Owner.In(s.ShopId);
        var worker = s.Worker.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Karim");

        var byOwner = await DebtAsync(owner, customer.Id, 100_000);
        var byWorker = await DebtAsync(worker, customer.Id, 70_000);
        byWorker.Transaction.CanEdit.Should().BeTrue();

        var foreign = await worker.DeleteAsync($"/api/transactions/{byOwner.Transaction.Id}");
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.CodeAsync()).Should().Be("transaction.forbidden");

        var patchForeign = await worker.PatchJsonAsync($"/api/transactions/{byOwner.Transaction.Id}", new UpdateTransactionRequest(1, null, null, null));
        patchForeign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var own = await (await worker.DeleteAsync($"/api/transactions/{byWorker.Transaction.Id}?reason=typo")).ReadAsync<DeleteTransactionResponse>();
        own.Balance.Should().Be(100_000);

        // the owner's view of the worker's entry after the window closes
        var late = await DebtAsync(worker, customer.Id, 5_000);
        fx.Clock.Advance(TimeSpan.FromMinutes(11));
        var tooLate = await worker.DeleteAsync($"/api/transactions/{late.Transaction.Id}");
        tooLate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var fetched = await (await worker.GetAsync($"/api/transactions/{late.Transaction.Id}")).ReadAsync<TransactionDto>();
        fetched.CanEdit.Should().BeFalse();
        (await (await owner.GetAsync($"/api/transactions/{late.Transaction.Id}")).ReadAsync<TransactionDto>()).CanEdit.Should().BeTrue();
    }

    [Fact]
    public async Task OwnerDelete_IsSoft_AndRecalculatesAllocations()
    {
        var s = await fx.ShopWithWorkerAsync("Ledger C");
        var owner = s.Owner.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Nodir");

        var debt = await DebtAsync(owner, customer.Id, 100_000);
        var payment = await PaymentAsync(owner, customer.Id, 60_000);
        (await (await owner.GetAsync($"/api/transactions/{debt.Transaction.Id}")).ReadAsync<TransactionDto>()).Remaining.Should().Be(40_000);

        var deleted = await (await owner.DeleteAsync($"/api/transactions/{payment.Transaction.Id}?reason=wrong%20customer")).ReadAsync<DeleteTransactionResponse>();
        deleted.Balance.Should().Be(100_000);
        (await (await owner.GetAsync($"/api/transactions/{debt.Transaction.Id}")).ReadAsync<TransactionDto>()).Remaining.Should().Be(100_000);
        (await owner.GetAsync($"/api/transactions/{payment.Transaction.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = fx.Scope();
        var db = scope.ServiceProvider.GetRequiredService<DebitsDbContext>();
        var row = await db.Transactions.IgnoreQueryFilters().SingleAsync(t => t.Id == payment.Transaction.Id);
        row.IsDeleted.Should().BeTrue();
        row.DeleteReason.Should().Be("wrong customer");
        row.DeletedByMemberId.Should().NotBeNull();
        (await db.AuditEvents.IgnoreQueryFilters().CountAsync(a => a.EntityId == payment.Transaction.Id.ToString() && a.Action == "transaction.delete")).Should().Be(1);
    }

    [Fact]
    public async Task TargetedPayment_CoversChosenDebtFirst_AndPatchCanRetarget()
    {
        var s = await fx.ShopWithWorkerAsync("Ledger D");
        var owner = s.Owner.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Olim");

        var first = await DebtAsync(owner, customer.Id, 100_000, new DateOnly(2026, 11, 1));
        fx.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await DebtAsync(owner, customer.Id, 50_000, new DateOnly(2026, 11, 15));
        fx.Clock.Advance(TimeSpan.FromSeconds(1));

        var payment = await PaymentAsync(owner, customer.Id, 50_000, target: second.Transaction.Id);
        (await (await owner.GetAsync($"/api/transactions/{second.Transaction.Id}")).ReadAsync<TransactionDto>()).Remaining.Should().Be(0);
        (await (await owner.GetAsync($"/api/transactions/{first.Transaction.Id}")).ReadAsync<TransactionDto>()).Remaining.Should().Be(100_000);

        var retargeted = await (await owner.PatchJsonAsync($"/api/transactions/{payment.Transaction.Id}", new UpdateTransactionRequest(null, null, null, Guid.Empty))).ReadAsync<LedgerMutation>();
        retargeted.Transaction.TargetDebtId.Should().BeNull();
        (await (await owner.GetAsync($"/api/transactions/{first.Transaction.Id}")).ReadAsync<TransactionDto>()).Remaining.Should().Be(50_000, "FIFO now covers the oldest debt");

        var edited = await (await owner.PatchJsonAsync($"/api/transactions/{first.Transaction.Id}", new UpdateTransactionRequest(120_000, "corrected", new DateOnly(2026, 12, 1), null))).ReadAsync<LedgerMutation>();
        edited.Transaction.Amount.Should().Be(120_000);
        edited.Transaction.Comment.Should().Be("corrected");
        edited.Transaction.DueDate.Should().Be(new DateOnly(2026, 12, 1));
        edited.Transaction.UpdatedAt.Should().NotBeNull();
        edited.Balance.Should().Be(120_000);

        var wrongTarget = await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Payment, 1_000, null, null, Guid.CreateVersion7()));
        wrongTarget.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongTarget.CodeAsync()).Should().Be("transaction.target_debt_invalid");
    }

    [Fact]
    public async Task Validation_RejectsBadAmounts_AndUnknownCustomer()
    {
        var s = await fx.ShopWithWorkerAsync("Ledger E");
        var owner = s.Owner.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Vali");

        var zero = await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 0, null, null, null));
        zero.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await zero.CodeAsync()).Should().Be("validation");

        var unknown = await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(Guid.CreateVersion7(), TransactionType.Debt, 1000, null, null, null));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.CodeAsync()).Should().Be("customer.not_found");

        var paymentWithDue = await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Payment, 1000, null, new DateOnly(2026, 11, 1), null));
        paymentWithDue.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await paymentWithDue.CodeAsync()).Should().Be("transaction.due_date_for_payment");
    }

    [Fact]
    public async Task Ledger_IsPaginated_NewestFirst()
    {
        var s = await fx.ShopWithWorkerAsync("Ledger F");
        var owner = s.Owner.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Paged");

        for (var i = 1; i <= 25; i++)
        {
            await DebtAsync(owner, customer.Id, i * 1000);
            fx.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        var all = new List<TransactionDto>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await (await owner.GetAsync($"/api/customers/{customer.Id}/ledger?limit=10&cursor={cursor}")).ReadAsync<Page<TransactionDto>>();
            page.Items.Count.Should().BeLessThanOrEqualTo(10);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        pages.Should().Be(3);
        all.Should().HaveCount(25);
        all.Select(t => t.Amount).Should().BeInDescendingOrder();
        all.Select(t => t.Id).Should().OnlyHaveUniqueItems();

        var onlyPayments = await (await owner.GetAsync($"/api/customers/{customer.Id}/ledger?type=payment")).ReadAsync<Page<TransactionDto>>();
        onlyPayments.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Transactions_AreInvisibleFromAnotherShop()
    {
        var a = await fx.ShopWithWorkerAsync("Ledger G1");
        var b = await fx.ShopWithWorkerAsync("Ledger G2");
        var customer = await CustomerAsync(a.Owner.In(a.ShopId), "Secret");
        var debt = await DebtAsync(a.Owner.In(a.ShopId), customer.Id, 1_000);

        (await b.Owner.In(b.ShopId).GetAsync($"/api/transactions/{debt.Transaction.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Owner.In(b.ShopId).GetAsync($"/api/customers/{customer.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Owner.In(b.ShopId).DeleteAsync($"/api/transactions/{debt.Transaction.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var foreignCustomer = await b.Owner.In(b.ShopId).PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 1000, null, null, null));
        foreignCustomer.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class EntriesApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ParseText_ReturnsDraft_WithCandidates_AndWritesNothing()
    {
        var s = await fx.ShopWithWorkerAsync("Parse A");
        var owner = s.Owner.In(s.ShopId);
        var ahmad = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Ahmad aka", null, null, null))).ReadAsync<CustomerDetail>();
        await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Zulfiya opa", "tikuvchi", null, null));
        await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("Zulfiya opa", "5-uy", null, null));

        var exact = await (await owner.PostJsonAsync("/api/entries/parse", new ParseEntryRequest("Ахмад ака 550 минг қарз, бир ҳафтага"))).ReadAsync<EntryParseResponse>();
        exact.Type.Should().Be(TransactionType.Debt);
        exact.Amount.Should().Be(550_000);
        exact.DueDate.Should().Be(fx.Today.AddDays(7));
        exact.DueDateIsDefault.Should().BeFalse();
        exact.CustomerStatus.Should().Be("exact");
        exact.CustomerId.Should().Be(ahmad.Id);
        exact.Parser.Should().Be("regex");

        var ambiguous = await (await owner.PostJsonAsync("/api/entries/parse", new ParseEntryRequest("Zulfiya opa 100 ming to'ladi"))).ReadAsync<EntryParseResponse>();
        ambiguous.Type.Should().Be(TransactionType.Payment);
        ambiguous.DueDate.Should().BeNull();
        ambiguous.CustomerStatus.Should().Be("ambiguous");
        ambiguous.Candidates.Select(c => c.Tag).Should().BeEquivalentTo(["tikuvchi", "5-uy"]);

        var none = await (await owner.PostJsonAsync("/api/entries/parse", new ParseEntryRequest("Yangi odam 20 ming"))).ReadAsync<EntryParseResponse>();
        none.CustomerStatus.Should().Be("none");
        none.CustomerName.Should().Be("Yangi odam");
        none.DueDate.Should().Be(fx.Today.AddDays(30));
        none.DueDateIsDefault.Should().BeTrue();

        var detail = await (await owner.GetAsync($"/api/customers/{ahmad.Id}")).ReadAsync<CustomerDetail>();
        detail.Balance.Should().Be(0);

        var empty = await owner.PostJsonAsync("/api/entries/parse", new ParseEntryRequest("  "));
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.CodeAsync()).Should().Be("entry.text_invalid");
    }

    [Fact]
    public async Task ParseAudio_WithoutGroqKey_SaysVoiceUnavailable()
    {
        var s = await fx.ShopWithWorkerAsync("Parse B");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "audio", "rec.webm");

        var response = await s.Owner.In(s.ShopId).PostAsync("/api/entries/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.CodeAsync()).Should().Be("voice.unavailable");
    }
}
