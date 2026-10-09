using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types.ReplyMarkups;

namespace Debits.Integration.Tests.Bot;

public class BotFlowTests(BotFixture fx) : IClassFixture<BotFixture>
{
    private static long _nextId = 500_000;
    private static TelegramUser NewUser(string name, string? lang = "uz") => new(Interlocked.Increment(ref _nextId), name, lang);

    private async Task<(TelegramUser Owner, Guid ShopId)> CreateShopViaWizardAsync(string shopName, string lang = "uz-Latn")
    {
        var owner = NewUser("Sarvar");
        await fx.SendTextAsync(owner, "/start");
        await fx.SendTextAsync(owner, shopName);
        await fx.PressAsync(owner, "w:due:30");
        await fx.PressAsync(owner, "w:lang:" + lang);

        using var scope = fx.Services.CreateScope();
        var shop = await fx.NewDb(scope).Shops.SingleAsync(s => s.Name == shopName);
        return (owner, shop.Id);
    }

    [Fact]
    public async Task Start_NewUser_RunsShopWizard_AndCreatesOwner()
    {
        var user = NewUser("Aziz", "ru");

        await fx.SendTextAsync(user, "/start");
        fx.LastTextTo(user.Id).Should().Contain("Здравствуйте, Aziz");

        await fx.SendTextAsync(user, "   ");
        fx.LastTextTo(user.Id).Should().Contain("от 1 до 64");

        await fx.SendTextAsync(user, "Do'kon Aziz");
        fx.LastTextTo(user.Id).Should().Contain("Do&#39;kon Aziz");
        fx.SentTo(user.Id).Last().ReplyMarkup.Should().BeOfType<InlineKeyboardMarkup>()
            .Which.InlineKeyboard.SelectMany(r => r).Select(b => b.CallbackData).Should().Contain(["w:due:15", "w:due:30", "w:due:45", "w:due:custom"]);

        await fx.PressAsync(user, "w:due:custom");
        fx.LastTextTo(user.Id).Should().Contain("1–365");
        await fx.SendTextAsync(user, "400");
        fx.LastTextTo(user.Id).Should().Contain("от 1 до 365");
        await fx.SendTextAsync(user, "20");
        fx.LastTextTo(user.Id).Should().Contain("Выберите язык");

        await fx.PressAsync(user, "w:lang:uz-Latn");

        using var scope = fx.Services.CreateScope();
        var db = fx.NewDb(scope);
        var appUser = await db.Users.SingleAsync(u => u.TelegramUserId == user.Id);
        var shop = await db.Shops.SingleAsync(s => s.Name == "Do'kon Aziz");
        shop.DefaultDueDays.Should().Be(20);
        shop.Language.Should().Be(AppLanguage.UzLatn);
        appUser.Language.Should().Be(AppLanguage.UzLatn);
        appUser.ActiveShopId.Should().Be(shop.Id);
        var member = await db.ShopMembers.IgnoreQueryFilters().SingleAsync(m => m.UserId == appUser.Id);
        member.Role.Should().Be(MemberRole.Owner);
        member.ShopId.Should().Be(shop.Id);
        (await db.ConversationStates.AnyAsync(s => s.UserId == appUser.Id)).Should().BeFalse();

        var texts = fx.SentTo(user.Id).Select(r => r.Text).Concat(fx.EditsIn(user.Id).Select(e => e.Text)).ToList();
        texts.Should().Contain(t => t.Contains("yaratildi"));          // wizard.done in the chosen language
        texts.Should().Contain(t => t.Contains("<b>Do&#39;kon Aziz</b>")); // menu
    }

    [Fact]
    public async Task Menu_ShowsShopRoleLanguage_AndWebAppButton()
    {
        var (owner, _) = await CreateShopViaWizardAsync("Menu Shop");
        fx.Reset();

        await fx.SendTextAsync(owner, "/menu");

        var msg = fx.SentTo(owner.Id).Single();
        msg.Text.Should().Contain("Menu Shop").And.Contain("egasi").And.Contain("O'zbekcha (lotin)");
        msg.ReplyMarkup.Should().BeOfType<InlineKeyboardMarkup>()
            .Which.InlineKeyboard.First().First().WebApp!.Url.Should().Be("https://app.example.com");
    }

    [Fact]
    public async Task Invite_OwnerCreatesLink_WorkerJoins_OwnerNotified()
    {
        var (owner, shopId) = await CreateShopViaWizardAsync("Invite Shop");
        fx.Reset();

        await fx.SendTextAsync(owner, "/invite");
        var inviteMsg = fx.SentTo(owner.Id).Single();
        inviteMsg.Text.Should().Contain("https://t.me/DebitsTestBot?start=inv_");
        var token = inviteMsg.Text.Split("?start=inv_")[1].Split('\n')[0].Trim();
        token.Length.Should().BeGreaterThan(15);

        var worker = NewUser("Karim", "ru");
        fx.Reset();
        await fx.SendTextAsync(worker, "/start inv_" + token);

        fx.SentTo(worker.Id).Select(r => r.Text).Should().Contain(t => t.Contains("присоединились к магазину «Invite Shop» как сотрудник"));
        fx.SentTo(owner.Id).Select(r => r.Text).Should().ContainSingle(t => t.Contains("Karim") && t.Contains("Invite Shop") && t.Contains("xodim"));

        using var scope = fx.Services.CreateScope();
        var db = fx.NewDb(scope);
        var workerUser = await db.Users.SingleAsync(u => u.TelegramUserId == worker.Id);
        workerUser.ActiveShopId.Should().Be(shopId);
        var membership = await db.ShopMembers.IgnoreQueryFilters().SingleAsync(m => m.UserId == workerUser.Id);
        membership.Role.Should().Be(MemberRole.Worker);
        membership.ShopId.Should().Be(shopId);
        var invite = await db.Invites.IgnoreQueryFilters().SingleAsync(i => i.Token == token);
        invite.UsedCount.Should().Be(1);

        // one-time link: second use fails
        var another = NewUser("Olim");
        fx.Reset();
        await fx.SendTextAsync(another, "/start inv_" + token);
        fx.LastTextTo(another.Id).Should().Contain("yaroqsiz");

        // worker cannot invite
        fx.Reset();
        await fx.SendTextAsync(worker, "/invite");
        fx.LastTextTo(worker.Id).Should().Contain("только владелец");
    }

    [Fact]
    public async Task Shops_SwitchesActiveShop()
    {
        var (owner, firstShopId) = await CreateShopViaWizardAsync("First Shop");
        fx.Reset();

        // create a second shop through /shops → new
        await fx.SendTextAsync(owner, "/shops");
        await fx.PressAsync(owner, "shop:new");
        await fx.SendTextAsync(owner, "Second Shop");
        await fx.PressAsync(owner, "w:due:15");
        await fx.PressAsync(owner, "w:lang:uz-Latn");

        using var scope = fx.Services.CreateScope();
        var db = fx.NewDb(scope);
        var user = await db.Users.SingleAsync(u => u.TelegramUserId == owner.Id);
        var second = await db.Shops.SingleAsync(s => s.Name == "Second Shop");
        user.ActiveShopId.Should().Be(second.Id);

        fx.Reset();
        await fx.SendTextAsync(owner, "/shops");
        var keyboard = fx.SentTo(owner.Id).Single().ReplyMarkup.Should().BeOfType<InlineKeyboardMarkup>().Subject;
        var buttons = keyboard.InlineKeyboard.SelectMany(r => r).ToList();
        buttons.Should().Contain(b => b.Text.StartsWith("✅ Second Shop"));
        buttons.Should().Contain(b => b.CallbackData == "shop:" + firstShopId.ToString("N"));

        await fx.PressAsync(owner, "shop:" + firstShopId.ToString("N"));
        fx.EditsIn(owner.Id).Last().Text.Should().Contain("First Shop");
        await db.Entry(user).ReloadAsync();
        user.ActiveShopId.Should().Be(firstShopId);
    }

    [Fact]
    public async Task Language_ChangesUserLanguage_AndMenuFollows()
    {
        var (owner, _) = await CreateShopViaWizardAsync("Lang Shop");
        fx.Reset();

        await fx.SendTextAsync(owner, "/language");
        await fx.PressAsync(owner, "lang:ru");
        fx.EditsIn(owner.Id).Last().Text.Should().Contain("Язык изменён: Русский");

        await fx.SendTextAsync(owner, "/menu");
        fx.SentTo(owner.Id).Last().Text.Should().Contain("владелец");

        using var scope = fx.Services.CreateScope();
        (await fx.NewDb(scope).Users.SingleAsync(u => u.TelegramUserId == owner.Id)).Language.Should().Be(AppLanguage.Ru);
    }

    [Fact]
    public async Task Cancel_AbortsWizard()
    {
        var user = NewUser("Bek");
        await fx.SendTextAsync(user, "/start");
        await fx.SendTextAsync(user, "/cancel");
        fx.LastTextTo(user.Id).Should().Contain("Bekor qilindi");

        using var scope = fx.Services.CreateScope();
        var db = fx.NewDb(scope);
        var appUser = await db.Users.SingleAsync(u => u.TelegramUserId == user.Id);
        (await db.ConversationStates.AnyAsync(s => s.UserId == appUser.Id)).Should().BeFalse();
        (await db.Shops.AnyAsync(s => s.Name == "/cancel")).Should().BeFalse();
    }

    [Fact]
    public async Task FreeText_WithoutShop_StartsWizard_WithShop_ShowsHint()
    {
        var user = NewUser("Nodir");
        await fx.SendTextAsync(user, "salom");
        fx.LastTextTo(user.Id).Should().Contain("do'kon nomini yozing");

        var (owner, _) = await CreateShopViaWizardAsync("Hint Shop");
        fx.Reset();
        await fx.SendTextAsync(owner, "Ahmad aka 550 ming");
        fx.LastTextTo(owner.Id).Should().Contain("tez orada");
    }

    [Fact]
    public async Task GroupMessages_AreIgnored()
    {
        var user = NewUser("Group");
        await fx.Dispatcher.DispatchAsync(new Telegram.Bot.Types.Update
        {
            Id = 1,
            Message = new Telegram.Bot.Types.Message
            {
                Text = "/start",
                From = user.ToTelegram(),
                Chat = new Telegram.Bot.Types.Chat { Id = -100, Type = Telegram.Bot.Types.Enums.ChatType.Group },
            },
        }, CancellationToken.None);

        using var scope = fx.Services.CreateScope();
        (await fx.NewDb(scope).Users.AnyAsync(u => u.TelegramUserId == user.Id)).Should().BeFalse();
    }
}
