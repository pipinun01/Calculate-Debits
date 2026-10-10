using Debits.Application.Tenancy;
using Debits.Bot.Entries;
using Debits.Domain.Entities;
using Debits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Debits.Integration.Tests.Bot;

/// <summary>Text / voice → confirmation card → ledger, driven through the dispatcher with a fake Telegram client and fake STT (regex parser only).</summary>
public class BotEntryTests(BotFixture fx) : IClassFixture<BotFixture>
{
    private static long _nextId = 900_000;
    private static TelegramUser NewUser(string name) => new(Interlocked.Increment(ref _nextId), name);

    private async Task<(TelegramUser Owner, Guid ShopId)> ShopAsync(string shopName)
    {
        var owner = NewUser("Sarvar");
        await fx.SendTextAsync(owner, "/start");
        await fx.SendTextAsync(owner, shopName);
        await fx.PressAsync(owner, "w:due:30");
        await fx.PressAsync(owner, "w:lang:uz-Latn");
        using var scope = fx.Services.CreateScope();
        var shop = await fx.NewDb(scope).Shops.SingleAsync(s => s.Name == shopName);
        return (owner, shop.Id);
    }

    private async Task<Guid> CustomerAsync(Guid shopId, string name, string? tag = null)
    {
        using var scope = fx.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetShop(shopId);
        var customer = Customer.Create(shopId, name, tag, null);
        var db = fx.NewDb(scope);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<T> DbAsync<T>(Func<DebitsDbContext, Task<T>> query)
    {
        using var scope = fx.Services.CreateScope();
        return await query(fx.NewDb(scope));
    }

    private Task<PendingEntry> LastPendingAsync(TelegramUser user) => DbAsync(db => db.PendingEntries
        .IgnoreQueryFilters()
        .Where(p => p.ChatId == user.Id)
        .OrderByDescending(p => p.Id)
        .FirstAsync());

    private Task<List<Transaction>> TransactionsAsync(Guid shopId) => DbAsync(db => db.Transactions
        .IgnoreQueryFilters()
        .Where(t => t.ShopId == shopId)
        .OrderBy(t => t.CreatedAt)
        .ToListAsync());

    private static string Pe(long id, string action) => $"pe:{id}:{action}";

    [Fact]
    public async Task Text_KnownCustomer_Card_Confirm_WritesDebt_AndReceipt()
    {
        var (owner, shopId) = await ShopAsync("Entry A");
        var customerId = await CustomerAsync(shopId, "Ahmad aka");

        await fx.SendTextAsync(owner, "Ahmad aka 550 ming qarz, bir haftaga");

        var card = fx.LastTextTo(owner.Id);
        card.Should().Contain("Ahmad aka 550 ming qarz").And.Contain("🏪 Entry A").And.Contain("👤 Ahmad aka").And.Contain("📕 Qarz")
            .And.Contain("550 000 so'm").And.Contain(EntryCard.Date(fx.Today.AddDays(7)));
        var pending = await LastPendingAsync(owner);
        pending.Status.Should().Be(PendingEntryStatus.Pending);
        pending.ExpiresAt.Should().Be(fx.Clock.GetUtcNow().UtcDateTime.AddMinutes(30));
        fx.LastButtons(owner.Id).Should().Contain([Pe(pending.Id, "ok"), Pe(pending.Id, "cu"), Pe(pending.Id, "amt"), Pe(pending.Id, "due"), Pe(pending.Id, "type"), Pe(pending.Id, "x")]);
        fx.LastButtons(owner.Id).Should().OnlyContain(d => System.Text.Encoding.UTF8.GetByteCount(d) <= 64);
        (await TransactionsAsync(shopId)).Should().BeEmpty("nothing is written before confirmation");

        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        var tx = (await TransactionsAsync(shopId)).Should().ContainSingle().Subject;
        tx.CustomerId.Should().Be(customerId);
        tx.Type.Should().Be(TransactionType.Debt);
        tx.Amount.Should().Be(550_000);
        tx.DueDate.Should().Be(fx.Today.AddDays(7));
        tx.Source.Should().Be(EntrySource.Text);
        tx.Transcript.Should().Be("Ahmad aka 550 ming qarz, bir haftaga");

        var receipt = fx.LastTextTo(owner.Id);
        receipt.Should().Contain("Qarz yozildi").And.Contain("✍️ Sarvar").And.Contain("Balans — Ahmad aka: <b>550 000 so'm</b>");
        fx.LastButtons(owner.Id).Should().Equal(Pe(pending.Id, "del"));
        (await LastPendingAsync(owner)).Status.Should().Be(PendingEntryStatus.Confirmed);
    }

    [Fact]
    public async Task DoubleConfirm_WritesOnce()
    {
        var (owner, shopId) = await ShopAsync("Entry B");
        await CustomerAsync(shopId, "Karim");
        await fx.SendTextAsync(owner, "Karim 100 ming qarz");
        var pending = await LastPendingAsync(owner);

        await fx.PressAsync(owner, Pe(pending.Id, "ok"));
        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        (await TransactionsAsync(shopId)).Should().ContainSingle();
        fx.LastToast().Should().Contain("allaqachon");
    }

    [Fact]
    public async Task TwoCustomersWithTheSameName_MustBePicked()
    {
        var (owner, shopId) = await ShopAsync("Entry C");
        await CustomerAsync(shopId, "Ahmad aka", "stomatolog");
        var butcher = await CustomerAsync(shopId, "Ahmad aka", "qassob");

        await fx.SendTextAsync(owner, "Ahmad aka 200 ming qarz");

        var pending = await LastPendingAsync(owner);
        fx.LastTextTo(owner.Id).Should().Contain("❓").And.Contain("Qaysi mijoz");
        var buttons = fx.LastButtons(owner.Id);
        buttons.Should().Contain([Pe(pending.Id, "cu:0"), Pe(pending.Id, "cu:1"), Pe(pending.Id, "cu:new")]).And.NotContain(Pe(pending.Id, "ok"));

        var payload = PendingPayload.Deserialize(pending.Payload);
        var index = payload.Candidates.FindIndex(c => c.Tag == "qassob");
        await fx.PressAsync(owner, Pe(pending.Id, $"cu:{index}"));

        fx.LastTextTo(owner.Id).Should().Contain("👤 Ahmad aka (qassob)");
        fx.LastButtons(owner.Id).Should().Contain(Pe(pending.Id, "ok"));

        await fx.PressAsync(owner, Pe(pending.Id, "ok"));
        (await TransactionsAsync(shopId)).Should().ContainSingle().Which.CustomerId.Should().Be(butcher);
    }

    [Fact]
    public async Task UnknownCustomer_IsCreatedWithTag_OnlyOnConfirmation()
    {
        var (owner, shopId) = await ShopAsync("Entry D");

        await fx.SendTextAsync(owner, "Vali aka 80 ming qarz");
        var pending = await LastPendingAsync(owner);
        fx.LastButtons(owner.Id).Should().Contain(Pe(pending.Id, "cu:new")).And.NotContain(Pe(pending.Id, "ok"));

        await fx.PressAsync(owner, Pe(pending.Id, "ok"));
        fx.LastToast().Should().Contain("mijozni tanlang");

        await fx.PressAsync(owner, Pe(pending.Id, "cu:new"));
        fx.LastTextTo(owner.Id).Should().Contain("«Vali aka»").And.Contain("belgi");
        fx.LastButtons(owner.Id).Should().Contain(Pe(pending.Id, "tag:skip"));

        await fx.SendTextAsync(owner, "5-uy");
        fx.LastTextTo(owner.Id).Should().Contain("🆕 Vali aka (5-uy)");
        (await DbAsync(db => db.Customers.IgnoreQueryFilters().CountAsync(c => c.ShopId == shopId))).Should().Be(0, "the customer is created together with the entry");

        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        var customer = await DbAsync(db => db.Customers.IgnoreQueryFilters().SingleAsync(c => c.ShopId == shopId));
        (customer.Name, customer.Tag).Should().Be(("Vali aka", "5-uy"));
        (await TransactionsAsync(shopId)).Should().ContainSingle().Which.CustomerId.Should().Be(customer.Id);
        fx.LastTextTo(owner.Id).Should().Contain("Balans — Vali aka (5-uy)");
    }

    [Fact]
    public async Task UnknownCustomer_SkipTag_AndCancel_LeavesNothing()
    {
        var (owner, shopId) = await ShopAsync("Entry E");

        await fx.SendTextAsync(owner, "Gulnora xola 40 ming qarz");
        var first = await LastPendingAsync(owner);
        await fx.PressAsync(owner, Pe(first.Id, "cu:new"));
        await fx.PressAsync(owner, Pe(first.Id, "tag:skip"));
        fx.LastTextTo(owner.Id).Should().Contain("🆕 Gulnora xola");
        await fx.PressAsync(owner, Pe(first.Id, "x"));

        fx.LastTextTo(owner.Id).Should().Contain("Bekor qilindi");
        (await LastPendingAsync(owner)).Status.Should().Be(PendingEntryStatus.Cancelled);
        (await DbAsync(db => db.Customers.IgnoreQueryFilters().CountAsync(c => c.ShopId == shopId))).Should().Be(0);
        (await TransactionsAsync(shopId)).Should().BeEmpty();

        await fx.PressAsync(owner, Pe(first.Id, "ok"));
        fx.LastToast().Should().Contain("allaqachon");

        // the same phrase again, this time confirmed without a tag
        await fx.SendTextAsync(owner, "Gulnora xola 40 ming qarz");
        var second = await LastPendingAsync(owner);
        await fx.PressAsync(owner, Pe(second.Id, "cu:new"));
        await fx.PressAsync(owner, Pe(second.Id, "tag:skip"));
        await fx.PressAsync(owner, Pe(second.Id, "ok"));
        var customer = await DbAsync(db => db.Customers.IgnoreQueryFilters().SingleAsync(c => c.ShopId == shopId));
        (customer.Name, customer.Tag).Should().Be(("Gulnora xola", null));
    }

    [Fact]
    public async Task Card_CanBeCorrected_Amount_Due_Type_Customer()
    {
        var (owner, shopId) = await ShopAsync("Entry F");
        await CustomerAsync(shopId, "Karim");
        var olim = await CustomerAsync(shopId, "Olim");

        await fx.SendTextAsync(owner, "Karim ellik qarz");
        var pending = await LastPendingAsync(owner);
        fx.LastTextTo(owner.Id).Should().Contain("<b>50 so'm</b>").And.Contain("⚠️");

        // amount
        await fx.PressAsync(owner, Pe(pending.Id, "amt"));
        await fx.SendTextAsync(owner, "tushunarsiz");
        fx.LastTextTo(owner.Id).Should().Contain("tushunmadim");
        await fx.SendTextAsync(owner, "ellik ming");
        fx.LastTextTo(owner.Id).Should().Contain("<b>50 000 so'm</b>").And.NotContain("⚠️");

        // due: preset, then a typed date, then a typed phrase
        await fx.PressAsync(owner, Pe(pending.Id, "due"));
        fx.LastButtons(owner.Id).Should().Contain([Pe(pending.Id, "due:3"), Pe(pending.Id, "due:7"), Pe(pending.Id, "due:30"), Pe(pending.Id, "due:eom"), Pe(pending.Id, "due:pick")]);
        await fx.PressAsync(owner, Pe(pending.Id, "due:3"));
        fx.LastTextTo(owner.Id).Should().Contain(EntryCard.Date(fx.Today.AddDays(3)));

        await fx.PressAsync(owner, Pe(pending.Id, "due:pick"));
        await fx.SendTextAsync(owner, "01.01.2020");
        fx.LastTextTo(owner.Id).Should().Contain("tushunmadim");
        var typed = fx.Today.AddDays(40);
        await fx.SendTextAsync(owner, typed.ToString("dd.MM.yyyy"));
        fx.LastTextTo(owner.Id).Should().Contain(EntryCard.Date(typed));

        await fx.PressAsync(owner, Pe(pending.Id, "due:pick"));
        await fx.SendTextAsync(owner, "2 haftaga");
        fx.LastTextTo(owner.Id).Should().Contain(EntryCard.Date(fx.Today.AddDays(14)));

        // customer: type another name
        await fx.PressAsync(owner, Pe(pending.Id, "cu"));
        fx.LastButtons(owner.Id).Should().Contain(Pe(pending.Id, "cu:type")).And.Contain(Pe(pending.Id, "back"));
        await fx.PressAsync(owner, Pe(pending.Id, "cu:type"));
        await fx.SendTextAsync(owner, "Olim");
        fx.LastTextTo(owner.Id).Should().Contain("👤 Olim");

        // type: payment has no term; back to debt restores the shop default
        await fx.PressAsync(owner, Pe(pending.Id, "type"));
        fx.LastTextTo(owner.Id).Should().Contain("📗 To'lov").And.NotContain("📅");
        fx.LastButtons(owner.Id).Should().NotContain(Pe(pending.Id, "due"));
        await fx.PressAsync(owner, Pe(pending.Id, "type"));
        fx.LastTextTo(owner.Id).Should().Contain("📕 Qarz").And.Contain(EntryCard.Date(fx.Today.AddDays(30)));
        await fx.PressAsync(owner, Pe(pending.Id, "type"));

        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        var tx = (await TransactionsAsync(shopId)).Should().ContainSingle().Subject;
        (tx.CustomerId, tx.Type, tx.Amount, tx.DueDate).Should().Be((olim, TransactionType.Payment, 50_000L, null));
        fx.LastTextTo(owner.Id).Should().Contain("To'lov yozildi").And.Contain("-50 000");
    }

    [Fact]
    public async Task Receipt_Delete_SoftDeletesTheTransaction()
    {
        var (owner, shopId) = await ShopAsync("Entry G");
        await CustomerAsync(shopId, "Nodir uka");
        await fx.SendTextAsync(owner, "Nodir uka 70 ming qarz");
        var pending = await LastPendingAsync(owner);
        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        await fx.PressAsync(owner, Pe(pending.Id, "del"));

        var tx = (await TransactionsAsync(shopId)).Should().ContainSingle().Subject;
        tx.IsDeleted.Should().BeTrue();
        fx.LastTextTo(owner.Id).Should().Contain("o'chirildi").And.Contain("Nodir uka: <b>0 so'm</b>");

        await fx.PressAsync(owner, Pe(pending.Id, "del"));
        fx.LastToast().Should().Contain("allaqachon");
    }

    [Fact]
    public async Task Voice_IsTranscribed_AndStoredWithTheEntry()
    {
        var (owner, shopId) = await ShopAsync("Entry H");
        await CustomerAsync(shopId, "Zulfiya opa");
        await fx.SendTextAsync(owner, "Zulfiya opa 300 ming qarz");
        await fx.PressAsync(owner, Pe((await LastPendingAsync(owner)).Id, "ok"));

        fx.Speech.NextText = "Зулфия опа 120 минг тўлади";
        await fx.SendVoiceAsync(owner, fileId: "voice-abc");

        var pending = await LastPendingAsync(owner);
        fx.Speech.LastNames.Should().Equal(["Zulfiya opa"], "the shop's customers are passed to the recogniser as a spelling hint");
        fx.LastTextTo(owner.Id).Should().Contain("🗣 «Зулфия опа 120 минг тўлади»").And.Contain("👤 Zulfiya opa").And.Contain("📗 To'lov").And.Contain("120 000");
        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        var payment = (await TransactionsAsync(shopId)).Single(t => t.Type == TransactionType.Payment);
        (payment.Source, payment.VoiceFileId, payment.Transcript, payment.DueDate).Should().Be((EntrySource.Voice, "voice-abc", "Зулфия опа 120 минг тўлади", null));
        fx.LastTextTo(owner.Id).Should().Contain("Balans — Zulfiya opa: <b>180 000 so'm</b>");
    }

    [Fact]
    public async Task Voice_TooLong_Failed_Empty_OrDisabled_AreExplained()
    {
        var (owner, shopId) = await ShopAsync("Entry I");
        var before = fx.Speech.Calls;

        await fx.SendVoiceAsync(owner, durationSeconds: 121);
        fx.LastTextTo(owner.Id).Should().Contain("120 soniya");
        fx.Speech.Calls.Should().Be(before, "long recordings are rejected before download");

        fx.Speech.NextError = new HttpRequestException("429");
        await fx.SendVoiceAsync(owner);
        fx.LastTextTo(owner.Id).Should().Contain("tanib bo'lmadi");
        fx.Speech.NextError = null;

        fx.Speech.NextText = "   ";
        await fx.SendVoiceAsync(owner);
        fx.LastTextTo(owner.Id).Should().Contain("eshitilmadi");

        fx.Speech.IsConfigured = false;
        await fx.SendVoiceAsync(owner);
        fx.LastTextTo(owner.Id).Should().Contain("yoqilmagan");
        fx.Speech.IsConfigured = true;

        (await DbAsync(db => db.PendingEntries.IgnoreQueryFilters().CountAsync(p => p.ShopId == shopId))).Should().Be(0);
    }

    [Fact]
    public async Task SmallTalk_IsNotAnEntry()
    {
        var (owner, shopId) = await ShopAsync("Entry J");

        await fx.SendTextAsync(owner, "salom, ishlar qalay");

        fx.LastTextTo(owner.Id).Should().Contain("/help");
        (await DbAsync(db => db.PendingEntries.IgnoreQueryFilters().CountAsync(p => p.ShopId == shopId))).Should().Be(0);
    }

    [Fact]
    public async Task EntryStaysInItsShop_WhenTheActiveShopChanges()
    {
        var (owner, firstShop) = await ShopAsync("Entry K1");
        await CustomerAsync(firstShop, "Aziz");
        await fx.SendTextAsync(owner, "Aziz 10 ming qarz");
        var pending = await LastPendingAsync(owner);

        // a second shop becomes active
        await fx.SendTextAsync(owner, "/shops");
        await fx.PressAsync(owner, "shop:new");
        await fx.SendTextAsync(owner, "Entry K2");
        await fx.PressAsync(owner, "w:due:15");
        await fx.PressAsync(owner, "w:lang:uz-Latn");
        var secondShop = await DbAsync(db => db.Shops.Where(s => s.Name == "Entry K2").Select(s => s.Id).SingleAsync());

        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        (await TransactionsAsync(firstShop)).Should().ContainSingle();
        (await TransactionsAsync(secondShop)).Should().BeEmpty();
        fx.LastTextTo(owner.Id).Should().Contain("🏪 Entry K1");
    }

    [Fact]
    public async Task ForeignUser_CannotPressSomeoneElsesCard()
    {
        var (owner, shopId) = await ShopAsync("Entry L");
        await CustomerAsync(shopId, "Bobur");
        await fx.SendTextAsync(owner, "Bobur 10 ming qarz");
        var pending = await LastPendingAsync(owner);
        var (stranger, _) = await ShopAsync("Entry L other");

        await fx.PressAsync(stranger, Pe(pending.Id, "ok"));

        (await TransactionsAsync(shopId)).Should().BeEmpty();
        (await LastPendingAsync(owner)).Status.Should().Be(PendingEntryStatus.Pending);
    }

    // The two tests below move the fixture clock ahead. Every test derives its dates from that clock, so the order does not matter.
    [Fact]
    public async Task ExpiredCard_CannotBeConfirmed()
    {
        var (owner, shopId) = await ShopAsync("Entry Y");
        await CustomerAsync(shopId, "Lola");
        await fx.SendTextAsync(owner, "Lola 60 ming qarz");
        var pending = await LastPendingAsync(owner);

        fx.Clock.Advance(TimeSpan.FromMinutes(31));
        await fx.PressAsync(owner, Pe(pending.Id, "ok"));

        (await TransactionsAsync(shopId)).Should().BeEmpty();
        (await LastPendingAsync(owner)).Status.Should().Be(PendingEntryStatus.Expired);
        fx.LastTextTo(owner.Id).Should().Contain("Vaqti o'tdi");
        fx.LastButtons(owner.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task Sweeper_ExpiresStaleCards_AndDeletesOldRows()
    {
        var (owner, shopId) = await ShopAsync("Entry Z");
        await CustomerAsync(shopId, "Anvar");
        await fx.SendTextAsync(owner, "Anvar 10 ming qarz");
        var old = await LastPendingAsync(owner);

        fx.Clock.Advance(TimeSpan.FromMinutes(45));
        await fx.SendTextAsync(owner, "Anvar 20 ming qarz");
        var fresh = await LastPendingAsync(owner);

        using (var scope = fx.Services.CreateScope())
        {
            var (expired, _) = await scope.ServiceProvider.GetRequiredService<PendingEntrySweeper>().SweepAsync();
            expired.Should().BeGreaterThanOrEqualTo(1);
        }

        (await DbAsync(db => db.PendingEntries.IgnoreQueryFilters().Where(p => p.Id == old.Id).Select(p => p.Status).SingleAsync())).Should().Be(PendingEntryStatus.Expired);
        (await DbAsync(db => db.PendingEntries.IgnoreQueryFilters().Where(p => p.Id == fresh.Id).Select(p => p.Status).SingleAsync())).Should().Be(PendingEntryStatus.Pending);

        fx.Clock.Advance(TimeSpan.FromHours(25));
        using (var scope = fx.Services.CreateScope())
        {
            var (_, deleted) = await scope.ServiceProvider.GetRequiredService<PendingEntrySweeper>().SweepAsync();
            deleted.Should().BeGreaterThanOrEqualTo(2);
        }

        (await DbAsync(db => db.PendingEntries.IgnoreQueryFilters().CountAsync(p => p.ShopId == shopId))).Should().Be(0);
    }
}
