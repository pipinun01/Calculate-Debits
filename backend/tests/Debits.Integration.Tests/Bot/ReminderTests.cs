using Debits.Application.Ledger;
using Debits.Application.Tenancy;
using Debits.Bot.Reminders;
using Debits.Domain.Entities;
using Debits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Debits.Integration.Tests.Bot;

/// <summary>Daily reminders against a real Postgres and a recording fake Telegram client. The fixture clock is 14:00 in Tashkent.</summary>
public class ReminderTests(BotFixture fx) : IClassFixture<BotFixture>
{
    private static long _nextId = 950_000;
    private static TelegramUser NewUser(string name, string language = "uz") => new(Interlocked.Increment(ref _nextId), name, language);

    private sealed record Shop(Guid Id, TelegramUser Owner, Guid OwnerMemberId, TelegramUser Worker, Guid WorkerMemberId);

    /// <summary>Shop with an Uzbek-speaking owner and a Russian-speaking worker.</summary>
    private async Task<Shop> ShopAsync(string shopName)
    {
        var owner = NewUser("Sarvar");
        await fx.SendTextAsync(owner, "/start");
        await fx.SendTextAsync(owner, shopName);
        await fx.PressAsync(owner, "w:due:30");
        await fx.PressAsync(owner, "w:lang:uz-Latn");

        fx.Reset();
        await fx.SendTextAsync(owner, "/invite");
        var token = fx.SentTo(owner.Id).Single().Text.Split("?start=inv_")[1].Split('\n')[0].Trim();
        var worker = NewUser("Karim", "ru");
        await fx.SendTextAsync(worker, "/start inv_" + token);

        var members = await DbAsync(db => db.ShopMembers.IgnoreQueryFilters()
            .Where(m => m.Shop.Name == shopName)
            .Select(m => new { m.Id, m.ShopId, m.Role })
            .ToListAsync());
        fx.Reset();
        return new Shop(members[0].ShopId, owner, members.Single(m => m.Role == MemberRole.Owner).Id, worker, members.Single(m => m.Role == MemberRole.Worker).Id);
    }

    private async Task<T> DbAsync<T>(Func<DebitsDbContext, Task<T>> query)
    {
        using var scope = fx.Services.CreateScope();
        return await query(fx.NewDb(scope));
    }

    private async Task<T> InShopAsync<T>(Shop shop, Guid memberId, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = fx.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(shop.Id, memberId, null, MemberRole.Owner, AppLanguage.UzLatn);
        return await action(scope.ServiceProvider);
    }

    private Task<Guid> CustomerAsync(Shop shop, string name, string? tag = null) => InShopAsync(shop, shop.OwnerMemberId, async sp =>
    {
        var customer = Customer.Create(shop.Id, name, tag, null);
        var db = sp.GetRequiredService<DebitsDbContext>();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    });

    /// <summary>A debt written yesterday (so it is older than today's reminder moment) unless <paramref name="justNow"/>.</summary>
    private async Task<Guid> DebtAsync(Shop shop, Guid customerId, long amount, DateOnly due, Guid? byMemberId = null, bool justNow = false)
    {
        var id = await InShopAsync(shop, byMemberId ?? shop.OwnerMemberId, async sp =>
            (await sp.GetRequiredService<LedgerService>().CreateAsync(new CreateTransactionCommand(customerId, TransactionType.Debt, amount, DueDate: due))).Transaction.Id);
        if (!justNow)
        {
            var yesterday = fx.Clock.GetUtcNow().UtcDateTime.AddDays(-1);
            await DbAsync(db => db.Transactions.IgnoreQueryFilters().Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, yesterday)));
        }

        return id;
    }

    private Task PayAsync(Shop shop, Guid customerId, long amount) => InShopAsync(shop, shop.OwnerMemberId, async sp =>
        (await sp.GetRequiredService<LedgerService>().CreateAsync(new CreateTransactionCommand(customerId, TransactionType.Payment, amount))).Transaction.Id);

    private async Task<int> RunDueAsync()
    {
        using var scope = fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderService>().RunDueAsync();
    }

    private async Task<int> RunShopAsync(Shop shop, DateOnly date)
    {
        using var scope = fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderService>().RunShopAsync(shop.Id, date);
    }

    private Task<List<ReminderLogEntry>> LogAsync(params Guid[] debtIds) =>
        DbAsync(db => db.ReminderLog.Where(r => debtIds.Contains(r.TransactionId)).OrderBy(r => r.Id).ToListAsync());

    [Fact]
    public async Task RunTwice_OneGroupedMessagePerKind_OneLogRowPerDebt()
    {
        var shop = await ShopAsync("Baraka");
        var ahmad = await CustomerAsync(shop, "Ahmad aka", "usta");
        var dilnoza = await CustomerAsync(shop, "Dilnoza opa");
        var botir = await CustomerAsync(shop, "Botir");

        var today1 = await DebtAsync(shop, ahmad, 550_000, fx.Today);
        var today2 = await DebtAsync(shop, dilnoza, 100_000, fx.Today, shop.WorkerMemberId);
        var in3 = await DebtAsync(shop, botir, 75_000, fx.Today.AddDays(3));
        var tomorrow = await DebtAsync(shop, botir, 10_000, fx.Today.AddDays(1));
        var overdue = await DebtAsync(shop, botir, 20_000, fx.Today.AddDays(-1));

        await RunDueAsync();
        await RunDueAsync();

        foreach (var chat in new[] { shop.Owner.Id, shop.Worker.Id })
        {
            fx.SentTo(chat).Should().HaveCount(2, "one message for debts due today and one for debts due in three days");
        }

        var todayText = fx.SentTo(shop.Owner.Id)[0].Text;
        todayText.Should().Contain("Bugun qarz muddati tugaydi").And.Contain("🏪 Baraka");
        todayText.Should().Contain("1. Ahmad aka (usta) — <b>550 000 so'm</b> · ✍️ Sarvar");
        todayText.Should().Contain("2. Dilnoza opa — <b>100 000 so'm</b> · ✍️ Karim");
        todayText.Should().Contain("Jami: 2 ta qarz, <b>650 000 so'm</b>");
        todayText.Should().NotContain("Botir");

        var in3Text = fx.SentTo(shop.Owner.Id)[1].Text;
        in3Text.Should().Contain("3 kundan keyin").And.Contain(fx.Today.AddDays(3).ToString("dd.MM"));
        in3Text.Should().Contain("1. Botir — <b>75 000 so'm</b>").And.NotContain("10 000").And.NotContain("20 000");

        var log = await LogAsync(today1, today2, in3, tomorrow, overdue);
        log.Select(r => (r.TransactionId, r.Kind, r.LocalDate)).Should().BeEquivalentTo(new[]
        {
            (today1, ReminderKind.DueToday, fx.Today),
            (today2, ReminderKind.DueToday, fx.Today),
            (in3, ReminderKind.DueIn3, fx.Today),
        });
    }

    [Fact]
    public async Task Recipient_GetsTextInOwnLanguage_WithButtonToDebtsOfThatShop()
    {
        var shop = await ShopAsync("Til do'koni");
        await DebtAsync(shop, await CustomerAsync(shop, "Ahmad aka"), 550_000, fx.Today);

        (await RunShopAsync(shop, fx.Today)).Should().Be(2);

        var toWorker = fx.SentTo(shop.Worker.Id).Single();
        toWorker.Text.Should().Contain("Сегодня срок долга").And.Contain("<b>550 000 сум</b>").And.Contain("Всего долгов: 1");
        var button = toWorker.ReplyMarkup.Should().BeOfType<InlineKeyboardMarkup>().Which.InlineKeyboard.Single().Single();
        button.Text.Should().Be("📒 Открыть долги");
        button.WebApp!.Url.Should().Be($"https://app.example.com/debts?shop={shop.Id}");
    }

    [Fact]
    public async Task PaidDebtIsSkipped_PartlyPaidShowsWhatIsLeft()
    {
        var shop = await ShopAsync("To'lov do'koni");
        var ahmad = await CustomerAsync(shop, "Ahmad aka");
        var botir = await CustomerAsync(shop, "Botir");
        var paid = await DebtAsync(shop, ahmad, 300_000, fx.Today);
        await PayAsync(shop, ahmad, 300_000);
        await DebtAsync(shop, botir, 500_000, fx.Today);
        await PayAsync(shop, botir, 120_000);

        await RunShopAsync(shop, fx.Today);

        var text = fx.SentTo(shop.Owner.Id).Single().Text;
        text.Should().Contain("1. Botir — <b>380 000 so'm</b>").And.NotContain("Ahmad");
        (await LogAsync(paid)).Should().BeEmpty();
    }

    [Fact]
    public async Task OwnersOnly_WorkerGetsNothing()
    {
        var shop = await ShopAsync("Egalar do'koni");
        await DbAsync(db => db.Shops.Where(s => s.Id == shop.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReminderRecipients, ReminderRecipients.Owners)));
        await DebtAsync(shop, await CustomerAsync(shop, "Ahmad aka"), 550_000, fx.Today);

        (await RunShopAsync(shop, fx.Today)).Should().Be(1);

        fx.SentTo(shop.Owner.Id).Should().ContainSingle();
        fx.SentTo(shop.Worker.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task BeforeReminderHour_Waits_AndDebtWrittenAfterItIsLeftForItsOwnDay()
    {
        var shop = await ShopAsync("Soat do'koni");
        var ahmad = await CustomerAsync(shop, "Ahmad aka");
        var old = await DebtAsync(shop, ahmad, 550_000, fx.Today);
        var fresh = await DebtAsync(shop, ahmad, 40_000, fx.Today, justNow: true);

        // 14:00 local now, reminders at 20:00: too early.
        await DbAsync(db => db.Shops.Where(s => s.Id == shop.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReminderHour, 20)));
        await RunDueAsync();
        fx.SentTo(shop.Owner.Id).Should().BeEmpty();
        (await LogAsync(old, fresh)).Should().BeEmpty();

        // Reminders at 09:00: the hour has passed, the run catches up; the debt written at 14:00 is not included.
        await DbAsync(db => db.Shops.Where(s => s.Id == shop.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReminderHour, 9)));
        await RunDueAsync();
        var text = fx.SentTo(shop.Owner.Id).Single().Text;
        text.Should().Contain("550 000").And.NotContain("40 000");
        (await LogAsync(old, fresh)).Select(r => r.TransactionId).Should().Equal(old);
    }

    [Fact]
    public async Task BlockedBot_PausesMember_UntilTheyWriteAgain()
    {
        var shop = await ShopAsync("Blok do'koni");
        var ahmad = await CustomerAsync(shop, "Ahmad aka");
        await DebtAsync(shop, ahmad, 550_000, fx.Today);

        fx.Client.SendRequest(Arg.Is<IRequest<Message>>(r => r is SendMessageRequest && ((SendMessageRequest)r).ChatId.Identifier == shop.Worker.Id), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Forbidden: bot was blocked by the user", 403));

        (await RunShopAsync(shop, fx.Today)).Should().Be(1, "the owner still gets the reminder");
        (await DbAsync(db => db.ShopMembers.IgnoreQueryFilters().SingleAsync(m => m.Id == shop.WorkerMemberId))).NotificationsBlockedAt.Should().NotBeNull();

        // Next kind of reminder for another debt: the blocked member is not even tried.
        fx.Reset();
        await DebtAsync(shop, ahmad, 60_000, fx.Today.AddDays(3));
        (await RunShopAsync(shop, fx.Today)).Should().Be(1);
        fx.SentTo(shop.Worker.Id).Should().BeEmpty();

        // The worker comes back and writes to the bot: reminders resume.
        fx.Client.SendRequest(Arg.Is<IRequest<Message>>(r => r is SendMessageRequest && ((SendMessageRequest)r).ChatId.Identifier == shop.Worker.Id), Arg.Any<CancellationToken>())
            .Returns(new Message());
        await fx.SendTextAsync(shop.Worker, "/help");
        (await DbAsync(db => db.ShopMembers.IgnoreQueryFilters().SingleAsync(m => m.Id == shop.WorkerMemberId))).NotificationsBlockedAt.Should().BeNull();
    }

    [Fact]
    public async Task LongList_IsFoldedIntoAndMore()
    {
        var shop = await ShopAsync("Katta do'kon");
        for (var i = 1; i <= ReminderMessage.MaxLines + 4; i++)
        {
            await DebtAsync(shop, await CustomerAsync(shop, $"Mijoz {i:00}"), 10_000, fx.Today);
        }

        await RunShopAsync(shop, fx.Today);

        var text = fx.SentTo(shop.Owner.Id).Single().Text;
        text.Should().Contain("30. Mijoz 30").And.NotContain("Mijoz 31").And.Contain("… va yana 4 ta").And.Contain("Jami: 34 ta qarz, <b>340 000 so'm</b>");
        text.Length.Should().BeLessThan(4096);
    }
}
