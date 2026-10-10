using System.Net;
using System.Net.Http.Json;
using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Api.Controllers;
using Debits.Application.Shops;

namespace Debits.Integration.Tests.Api;

public class AuthTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ValidInitData_IssuesJwt_AndCreatesUser()
    {
        var tgId = fx.NextTelegramId();
        using var client = fx.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/telegram", new TelegramAuthRequest(fx.SignInitData(tgId, "Sarvar", "sarvar", "ru")), ApiFixture.Json);

        var auth = await response.ReadAsync<AuthResponse>();
        auth.Token.Should().NotBeNullOrEmpty();
        auth.ExpiresAt.Should().Be(fx.Clock.GetUtcNow().AddHours(12));
        auth.User.TelegramUserId.Should().Be(tgId);
        auth.User.FirstName.Should().Be("Sarvar");
        auth.User.Username.Should().Be("sarvar");
        auth.User.Language.Should().Be(Domain.Entities.AppLanguage.Ru);
        auth.User.Shops.Should().BeEmpty();
    }

    [Fact]
    public async Task TamperedHash_IsRejected()
    {
        using var client = fx.Factory.CreateClient();
        var signedWithOtherToken = fx.SignInitData(fx.NextTelegramId(), "Mallory", botToken: "999:OTHER");

        var response = await client.PostAsJsonAsync("/api/auth/telegram", new TelegramAuthRequest(signedWithOtherToken), ApiFixture.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.CodeAsync()).Should().Be("auth.init_data_invalid");
    }

    [Fact]
    public async Task StaleInitData_IsRejected()
    {
        using var client = fx.Factory.CreateClient();
        var stale = fx.SignInitData(fx.NextTelegramId(), "Old", authDate: fx.Clock.GetUtcNow().AddHours(-25));

        var response = await client.PostAsJsonAsync("/api/auth/telegram", new TelegramAuthRequest(stale), ApiFixture.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.CodeAsync()).Should().Be("auth.init_data_expired");
    }

    [Fact]
    public async Task DevHeader_BypassesHmac_InDevelopment()
    {
        var tgId = fx.NextTelegramId();
        using var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthController.DevTelegramIdHeader, tgId.ToString());
        client.DefaultRequestHeaders.Add(AuthController.DevFirstNameHeader, "Browser");

        var response = await client.PostAsJsonAsync("/api/auth/telegram", new TelegramAuthRequest(null), ApiFixture.Json);

        var auth = await response.ReadAsync<AuthResponse>();
        auth.User.TelegramUserId.Should().Be(tgId);
        auth.User.FirstName.Should().Be("Browser");
    }

    [Fact]
    public async Task MissingToken_Returns401Problem()
    {
        using var client = fx.Factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.CodeAsync()).Should().Be("auth.unauthorized");
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var session = await fx.LoginAsync("Short");
        using var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);

        fx.Clock.Advance(TimeSpan.FromHours(13));
        var response = await client.GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.CodeAsync()).Should().Be("auth.unauthorized");
    }

    [Fact]
    public async Task Me_And_Shops_RoundTrip()
    {
        var session = await fx.LoginAsync("Owner");

        var created = await session.Client.PostJsonAsync("/api/shops", new CreateShopRequest("Do'kon 1", 20, Domain.Entities.AppLanguage.UzCyrl));
        var shop = await created.ReadAsync<CreateShopResponse>();
        shop.Shop.DefaultDueDays.Should().Be(20);
        shop.Shop.Language.Should().Be(Domain.Entities.AppLanguage.UzCyrl);
        shop.Membership.Role.Should().Be(Domain.Entities.MemberRole.Owner);

        var me = await (await session.Client.GetAsync("/api/me")).ReadAsync<MeDto>();
        me.ActiveShopId.Should().Be(shop.Shop.Id);
        me.Shops.Should().ContainSingle(s => s.ShopId == shop.Shop.Id && s.IsActive);

        var shops = await (await session.Client.GetAsync("/api/shops")).ReadAsync<List<ShopMembershipInfo>>();
        shops.Should().ContainSingle();

        var patched = await (await session.Client.PatchJsonAsync("/api/me", new UpdateMeRequest(Domain.Entities.AppLanguage.Ru, null))).ReadAsync<MeDto>();
        patched.Language.Should().Be(Domain.Entities.AppLanguage.Ru);
    }

    [Fact]
    public async Task ShopHeader_ForForeignShop_Is403()
    {
        var a = await fx.LoginAsync("A");
        var b = await fx.LoginAsync("B");
        var shop = await (await a.Client.PostJsonAsync("/api/shops", new CreateShopRequest("A shop", null, null))).ReadAsync<CreateShopResponse>();

        var response = await b.In(shop.Shop.Id).GetAsync("/api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.CodeAsync()).Should().Be("shop.not_member");
    }

    [Fact]
    public async Task MissingShopHeader_OnTenantEndpoint_Is400()
    {
        var session = await fx.LoginAsync("NoShop");

        var response = await session.Client.GetAsync("/api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.CodeAsync()).Should().Be("shop.required");
    }

    [Fact]
    public async Task InvalidShopHeader_Is400()
    {
        var session = await fx.LoginAsync("Bad");
        using var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        client.DefaultRequestHeaders.Add(TenantMiddleware.ShopHeader, "not-a-guid");

        var response = await client.GetAsync("/api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.CodeAsync()).Should().Be("shop.invalid");
    }
}
