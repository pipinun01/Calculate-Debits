using System.Globalization;
using System.Net;
using ClosedXML.Excel;
using Debits.Api.Contracts;
using Debits.Api.Controllers;
using Debits.Application.Audit;
using Debits.Application.Common;
using Debits.Application.Customers;
using Debits.Application.Ledger;
using Debits.Domain.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Debits.Integration.Tests.Api;

/// <summary>Excel files, the Telegram summary and the audit trail they leave.</summary>
public class ExportApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static async Task<CustomerDetail> CustomerAsync(HttpClient client, string name, string? tag = null, string? phone = null) =>
        await (await client.PostJsonAsync("/api/customers", new CreateCustomerRequest(name, tag, phone, null))).ReadAsync<CustomerDetail>();

    private static async Task<LedgerMutation> EntryAsync(HttpClient client, Guid customerId, TransactionType type, long amount, DateOnly? due = null, string? comment = null) =>
        await (await client.PostJsonAsync("/api/transactions", new CreateTransactionRequest(customerId, type, amount, comment, due, null))).ReadAsync<LedgerMutation>();

    private static async Task<XLWorkbook> WorkbookAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(Xlsx);
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }

    /// <summary>All non-empty cell texts of a sheet, row by row.</summary>
    private static List<List<string>> Rows(IXLWorksheet ws) =>
        ws.RowsUsed().Select(r => r.CellsUsed().Select(Text).ToList()).ToList();

    /// <summary>Numbers are rendered here with the invariant culture: Excel applies the reader's own separators.</summary>
    private static string Text(IXLCell cell)
    {
        if (cell.DataType != XLDataType.Number)
        {
            return cell.GetFormattedString();
        }

        var value = cell.GetDouble();
        return cell.Style.NumberFormat.Format.Contains('%')
            ? Math.Round(value * 100).ToString(CultureInfo.InvariantCulture) + "%"
            : value.ToString("#,0", CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<T> Sent<T>(long chatId) where T : class => fx.Telegram.ReceivedCalls()
        .Select(c => c.GetArguments().FirstOrDefault())
        .OfType<T>()
        .Where(r => r switch
        {
            SendDocumentRequest d => d.ChatId.Identifier == chatId,
            SendMessageRequest m => m.ChatId.Identifier == chatId,
            _ => false,
        })
        .ToList();

    [Fact]
    public async Task CustomerLedger_File_HasEveryEntryWithRunningBalance()
    {
        var s = await fx.ShopWithWorkerAsync("Eksport A");
        var owner = s.Owner.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Ahmad aka", "usta", "+998901234567");
        await EntryAsync(owner, customer.Id, TransactionType.Debt, 550_000, fx.Today.AddDays(7), "un, shakar");
        await EntryAsync(s.Worker.In(s.ShopId), customer.Id, TransactionType.Payment, 200_000);
        await EntryAsync(owner, customer.Id, TransactionType.Debt, 80_000, fx.Today.AddDays(30));

        var response = await owner.GetAsync($"/api/export/customers/{customer.Id}");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().StartWith("Debits_Ahmad_aka_(usta)_").And.EndWith(".xlsx");
        using var workbook = await WorkbookAsync(response);

        var ws = workbook.Worksheets.Single();
        ws.Name.Should().Be("Tarix");
        var rows = Rows(ws);
        rows.Should().ContainEquivalentOf(new[] { "Do'kon", "Eksport A" });
        rows.Should().ContainEquivalentOf(new[] { "Mijoz", "Ahmad aka (usta)" });
        rows.Should().ContainEquivalentOf(new[] { "Telefon", "+998901234567" });
        rows.Should().ContainEquivalentOf(new[] { "Balans, so'm", "430,000" });

        var header = rows.FindIndex(r => r.FirstOrDefault() == "Sana");
        rows[header].Should().Equal("Sana", "Turi", "Summa, so'm", "Muddat", "Qoldiq, so'm", "Izoh", "Yozgan", "Balans, so'm");
        var entries = rows.Skip(header + 1).ToList();
        entries.Should().HaveCount(3);
        // The payment went to the oldest debt first: 550 000 − 200 000 is still open.
        entries[0].Skip(1).Should().Equal("Qarz", "550,000", fx.Today.AddDays(7).ToString("dd.MM.yyyy"), "350,000", "un, shakar", "Sarvar", "550,000");
        entries[1].Skip(1).Should().Equal("To'lov", "200,000", "Aziz", "350,000");
        entries[2].Skip(1).Should().Equal("Qarz", "80,000", fx.Today.AddDays(30).ToString("dd.MM.yyyy"), "80,000", "Sarvar", "430,000");

        // The worker may take one customer's page too, a stranger's customer is a 404.
        (await s.Worker.In(s.ShopId).GetAsync($"/api/export/customers/{customer.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var other = await fx.ShopWithWorkerAsync("Eksport A2");
        var foreign = await other.Owner.In(other.ShopId).GetAsync($"/api/export/customers/{customer.Id}");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.CodeAsync()).Should().Be("customer.not_found");
    }

    [Fact]
    public async Task ShopFile_HasSummaryDebtorsAndEntries_InTheUsersLanguage_OwnersOnly()
    {
        var s = await fx.ShopWithWorkerAsync("Eksport B");
        var owner = s.Owner.In(s.ShopId);
        (await owner.PatchJsonAsync("/api/me", new UpdateMeRequest(AppLanguage.Ru, null))).EnsureSuccessStatusCode();
        var ahmad = await CustomerAsync(owner, "Ahmad aka");
        var botir = await CustomerAsync(owner, "Botir", phone: "+998935550011");
        await EntryAsync(owner, ahmad.Id, TransactionType.Debt, 500_000, fx.Today.AddDays(10));
        await EntryAsync(owner, ahmad.Id, TransactionType.Payment, 500_000);
        await EntryAsync(owner, botir.Id, TransactionType.Debt, 300_000, fx.Today.AddDays(5));
        await EntryAsync(owner, botir.Id, TransactionType.Payment, 100_000);

        using var workbook = await WorkbookAsync(await owner.GetAsync($"/api/export/shop?from={fx.Today:yyyy-MM-dd}&to={fx.Today:yyyy-MM-dd}"));
        workbook.Worksheets.Select(w => w.Name).Should().Equal("Отчёт", "Должники", "Операции");

        var summary = Rows(workbook.Worksheet("Отчёт"));
        summary.Should().ContainEquivalentOf(new[] { "Магазин", "Eksport B" });
        summary.Should().ContainEquivalentOf(new[] { "Период", $"{fx.Today:dd.MM.yyyy} — {fx.Today:dd.MM.yyyy}" });
        summary.Should().ContainEquivalentOf(new[] { "Собрано, сум", "600,000" });
        summary.Should().ContainEquivalentOf(new[] { "Новые долги, сум", "800,000" });
        summary.Should().ContainEquivalentOf(new[] { "Возвращаемость", "75%" });
        summary.Should().ContainEquivalentOf(new[] { "Открытые долги, сум", "200,000" });

        var debtors = Rows(workbook.Worksheet("Должники"));
        debtors.Should().HaveCount(3, "header, the only debtor, total");
        debtors[1].Should().Equal("Botir", "+998935550011", "200,000", "0", fx.Today.AddDays(5).ToString("dd.MM.yyyy"));
        debtors[2].Should().Equal("Итого", "200,000", "0");

        var entries = Rows(workbook.Worksheet("Операции"));
        entries[0].Should().Equal("Дата", "Клиент", "Тип", "Сумма, сум", "Срок", "Остаток, сум", "Комментарий", "Записал");
        entries.Skip(1).Select(r => (r[1], r[2], r[3])).Should().Equal(
            ("Ahmad aka", "Долг", "500,000"), ("Ahmad aka", "Оплата", "500,000"), ("Botir", "Долг", "300,000"), ("Botir", "Оплата", "100,000"));

        var worker = await s.Worker.In(s.ShopId).GetAsync("/api/export/shop");
        worker.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await worker.CodeAsync()).Should().Be("forbidden.owner_only");
    }

    [Fact]
    public async Task Send_DeliversFilesAndSummaryToTheCallersChat()
    {
        var s = await fx.ShopWithWorkerAsync("Eksport C");
        var owner = s.Owner.In(s.ShopId);
        var chat = s.Owner.User.TelegramUserId;
        var customer = await CustomerAsync(owner, "Dilnoza opa");
        await EntryAsync(owner, customer.Id, TransactionType.Debt, 250_000, fx.Today.AddDays(3));
        await EntryAsync(owner, customer.Id, TransactionType.Payment, 50_000);

        (await owner.PostAsync($"/api/export/customers/{customer.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var ledger = Sent<SendDocumentRequest>(chat).Single();
        ledger.Caption.Should().Contain("Dilnoza opa").And.Contain("200 000 so'm");
        ((InputFileStream)ledger.Document).FileName.Should().StartWith("Debits_Dilnoza_opa_").And.EndWith(".xlsx");

        (await owner.PostJsonAsync("/api/export/shop/send", new PeriodRequest(fx.Today, fx.Today))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Sent<SendDocumentRequest>(chat).Should().HaveCount(2);
        Sent<SendDocumentRequest>(chat)[1].Caption.Should().Contain("Eksport C").And.Contain(fx.Today.ToString("dd.MM.yyyy"));

        (await s.Worker.In(s.ShopId).PostJsonAsync("/api/reports/send", new PeriodRequest(fx.Today, fx.Today))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var text = Sent<SendMessageRequest>(s.Worker.User.TelegramUserId).Single().Text;
        text.Should().Contain("<b>Hisobot</b> · Eksport C").And.Contain($"{fx.Today:dd.MM.yyyy} — {fx.Today:dd.MM.yyyy}");
        text.Should().Contain("Yig'ildi: <b>50 000 so'm</b> (1 ta to'lov)");
        text.Should().Contain("Yangi qarzlar: <b>250 000 so'm</b> (1 ta)");
        text.Should().Contain("Qaytish darajasi: 20%");
        text.Should().Contain("Ochiq qarzlar: <b>200 000 so'm</b>");
        text.Should().Contain("1. Dilnoza opa — 200 000 so'm");
        Sent<SendMessageRequest>(chat).Should().BeEmpty("the summary goes only to the one who asked");
    }

    [Fact]
    public async Task Send_WhenTheUserBlockedTheBot_Is409WithACode()
    {
        var s = await fx.ShopWithWorkerAsync("Eksport D");
        var chat = s.Owner.User.TelegramUserId;
        fx.Telegram.SendRequest(Arg.Is<IRequest<Message>>(r => r is SendMessageRequest && ((SendMessageRequest)r).ChatId.Identifier == chat), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Forbidden: bot was blocked by the user", 403));

        var response = await s.Owner.In(s.ShopId).PostJsonAsync("/api/reports/send", new PeriodRequest(null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.CodeAsync()).Should().Be("bot.blocked");
    }

    [Fact]
    public async Task Audit_ListsWhoDidWhat_NewestFirst_OwnersOnly()
    {
        var s = await fx.ShopWithWorkerAsync("Audit A");
        var owner = s.Owner.In(s.ShopId);
        var worker = s.Worker.In(s.ShopId);
        var customer = await CustomerAsync(owner, "Ahmad aka", "usta");
        var debt = await EntryAsync(worker, customer.Id, TransactionType.Debt, 90_000, fx.Today.AddDays(7));
        (await owner.DeleteAsync($"/api/transactions/{debt.Transaction.Id}?reason=xato")).EnsureSuccessStatusCode();
        (await owner.GetAsync($"/api/export/customers/{customer.Id}")).EnsureSuccessStatusCode();
        (await owner.PatchJsonAsync("/api/shop", new UpdateShopRequest(null, 15, null, null, null, null))).EnsureSuccessStatusCode();

        var page = await (await owner.GetAsync("/api/audit?limit=3")).ReadAsync<Page<AuditEventDto>>();
        page.Items.Select(e => e.Action).Should().Equal("shop.update", "export.customer", "transaction.delete");
        page.NextCursor.Should().NotBeNull();
        page.Items[1].Should().BeEquivalentTo(new { MemberName = "Sarvar", Subject = "Ahmad aka (usta)", Entity = "customer" });
        page.Items[2].Should().BeEquivalentTo(new { MemberName = "Sarvar", Subject = "Ahmad aka (usta)", Amount = 90_000L, TransactionType = TransactionType.Debt },
            "a deleted entry still shows whose and how much it was");

        var rest = await (await owner.GetAsync($"/api/audit?limit=3&cursor={page.NextCursor}")).ReadAsync<Page<AuditEventDto>>();
        rest.Items.Select(e => e.Action).Should().Equal("transaction.create", "customer.create");
        rest.Items[0].MemberName.Should().Be("Aziz");
        rest.NextCursor.Should().BeNull();

        var forbidden = await worker.GetAsync("/api/audit");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.CodeAsync()).Should().Be("forbidden.owner_only");
    }
}
