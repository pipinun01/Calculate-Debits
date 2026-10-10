using Debits.Api.Contracts;
using Debits.Api.Controllers;
using Debits.Application.Customers;
using Debits.Application.Debts;
using Debits.Application.Reports;
using Debits.Domain.Entities;

namespace Debits.Integration.Tests.Api;

public class ReportsApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Debts_SummaryAndStatuses()
    {
        var s = await fx.ShopWithWorkerAsync("Reports A");
        var owner = s.Owner.In(s.ShopId);
        var today = fx.Today;

        var a = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("A", null, null, null))).ReadAsync<CustomerDetail>();
        var b = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("B", null, null, null))).ReadAsync<CustomerDetail>();
        var c = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("C", null, null, null))).ReadAsync<CustomerDetail>();
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(a.Id, TransactionType.Debt, 300_000, null, today.AddDays(-3), null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(a.Id, TransactionType.Payment, 100_000, null, null, null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(b.Id, TransactionType.Debt, 200_000, null, today.AddDays(3), null));
        await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(c.Id, TransactionType.Debt, 500_000, null, today.AddDays(30), null));

        var all = await (await owner.GetAsync("/api/debts")).ReadAsync<DebtsResponse>();
        all.Summary.Today.Should().Be(today);
        all.Summary.Total.Should().Be(900_000);
        all.Summary.Overdue.Should().Be(200_000);
        all.Summary.DueSoon.Should().Be(200_000);
        all.Summary.OnTime.Should().Be(500_000);
        all.Summary.OverdueCount.Should().Be(1);
        all.Summary.DebtorsCount.Should().Be(3);
        all.Summary.ActiveCustomers.Should().Be(3);
        all.Items.Select(d => d.CustomerName).Should().ContainInOrder("A", "B", "C");
        all.Items[0].DaysLeft.Should().Be(-3);
        all.Items[0].Remaining.Should().Be(200_000);
        all.Items[0].CreatedByName.Should().Be("Sarvar");

        (await (await owner.GetAsync("/api/debts?status=overdue")).ReadAsync<DebtsResponse>()).Items.Should().ContainSingle(d => d.CustomerId == a.Id);
        (await (await owner.GetAsync("/api/debts?status=due_soon")).ReadAsync<DebtsResponse>()).Items.Should().ContainSingle(d => d.CustomerId == b.Id);
        (await (await owner.GetAsync("/api/debts?status=onTime")).ReadAsync<DebtsResponse>()).Items.Should().ContainSingle(d => d.CustomerId == c.Id);
        (await (await owner.GetAsync("/api/debts/summary")).ReadAsync<DebtSummary>()).Total.Should().Be(900_000);
    }

    [Fact]
    public async Task Summary_ComparesWithPreviousPeriod_AndBuckets()
    {
        var s = await fx.ShopWithWorkerAsync("Reports B");
        var owner = s.Owner.In(s.ShopId);
        var customer = await (await owner.PostJsonAsync("/api/customers", new CreateCustomerRequest("R", null, null, null))).ReadAsync<CustomerDetail>();

        // The fake clock only moves forward: work in the month after next, so "previous month" is still ahead of the other tests' data.
        var now = fx.Clock.GetUtcNow();
        var month = new DateTimeOffset(now.Year, now.Month, 1, 9, 0, 0, TimeSpan.Zero).AddMonths(2);
        var from = DateOnly.FromDateTime(month.UtcDateTime);
        var to = from.AddMonths(1).AddDays(-1);

        // previous month: 1 debt (each jump outlives the 12h JWT, hence the re-logins)
        fx.Clock.SetUtcNow(month.AddMonths(-1).AddDays(14));
        owner = await fx.ReloginAsync(s.Owner, s.ShopId);
        (await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 100_000, null, null, null))).EnsureSuccessStatusCode();
        // this month: 2 debts, 1 payment
        fx.Clock.SetUtcNow(month.AddDays(1));
        owner = await fx.ReloginAsync(s.Owner, s.ShopId);
        (await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 250_000, null, null, null))).EnsureSuccessStatusCode();
        fx.Clock.SetUtcNow(month.AddDays(8).AddHours(11).AddMinutes(30)); // 20:30 UTC on the 9th = 01:30 on the 10th in Tashkent
        owner = await fx.ReloginAsync(s.Owner, s.ShopId);
        (await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Payment, 150_000, null, null, null))).EnsureSuccessStatusCode();
        (await owner.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customer.Id, TransactionType.Debt, 50_000, null, null, null))).EnsureSuccessStatusCode();
        fx.Clock.SetUtcNow(month.AddDays(9));
        owner = await fx.ReloginAsync(s.Owner, s.ShopId);

        var summary = await (await owner.GetAsync("/api/reports/summary")).ReadAsync<ReportSummary>();
        summary.From.Should().Be(from);
        summary.To.Should().Be(to);
        summary.NewDebts.Should().Be(300_000);
        summary.NewDebtsPrevious.Should().Be(100_000);
        summary.Collected.Should().Be(150_000);
        summary.CollectedPrevious.Should().Be(0);
        summary.CollectionRate.Should().Be(0.5);
        summary.OpenTotal.Should().Be(250_000);
        summary.Flow.Should().HaveCount((to.Day + 6) / 7, "7-day buckets over the month");
        summary.Flow[0].Debts.Should().Be(250_000);
        summary.Flow[1].Debts.Should().Be(50_000);
        summary.Flow[1].Payments.Should().Be(150_000);
        summary.TopDebtors.Should().ContainSingle().Which.Balance.Should().Be(250_000);

        var collections = await (await owner.GetAsync($"/api/reports/collections?from={from:yyyy-MM-dd}&to={from.AddDays(9):yyyy-MM-dd}")).ReadAsync<List<CollectionPoint>>();
        collections.Should().HaveCount(2);
        collections[1].Date.Should().Be(from.AddDays(9), "UTC evening of the 9th is already the 10th in Tashkent");
        collections[1].Payments.Should().Be(150_000);

        var top = await (await owner.GetAsync("/api/reports/top-debtors?limit=3")).ReadAsync<List<TopDebtor>>();
        top.Should().ContainSingle().Which.Name.Should().Be("R");

        var bad = await owner.GetAsync("/api/reports/summary?from=2026-10-10&to=2026-10-01");
        (await bad.CodeAsync()).Should().Be("report.range_invalid");
    }
}
