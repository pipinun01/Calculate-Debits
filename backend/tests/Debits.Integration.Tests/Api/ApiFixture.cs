using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Api.Json;
using Debits.Application.Members;
using Debits.Bot;
using Debits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Telegram.Bot;
using Testcontainers.PostgreSql;

namespace Debits.Integration.Tests.Api;

/// <summary>Full HTTP pipeline (JWT → TenantMiddleware → policies → controllers) over a Testcontainers Postgres, bot hosted services removed.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string BotToken = "123456789:TESTTOKENabcdefghijklmnopqrstuvwxyz";
    public const string JwtSecret = "integration-test-secret-0123456789-abcdef";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("debits_api")
        .WithUsername("debits")
        .WithPassword("debits")
        .Build();

    private long _nextTelegramId = 700_000;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    public ITelegramBotClient Telegram { get; } = Substitute.For<ITelegramBotClient>();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new AppLanguageJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public long NextTelegramId() => Interlocked.Increment(ref _nextTelegramId);

    /// <summary>Today in Asia/Tashkent (UTC+5, no DST) by the fake clock. The clock only moves forward, so tests derive dates from it.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime.AddHours(5));

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // ConfigureAppConfiguration of the factory lands below appsettings.*.json for minimal hosting (it does not override them),
        // so the test overrides go straight into DI: fresh DbContext options and PostConfigure for the option classes.
        var connectionString = _container.GetConnectionString();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<DebitsDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<DebitsDbContext>(o => DebitsDbContextOptions.Configure(o, connectionString));

                services.PostConfigure<JwtOptions>(o => o.Secret = JwtSecret);
                services.PostConfigure<BotOptions>(o =>
                {
                    o.Token = BotToken;
                    o.Username = "DebitsTestBot";
                    o.Mode = "polling";
                });
                services.PostConfigure<MiniAppOptions>(o => o.Url = "https://app.example.com");
                // The developer's .env may hold a real key: tests must never call Groq.
                services.PostConfigure<Debits.Infrastructure.Groq.GroqOptions>(o => o.ApiKey = string.Empty);

                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Namespace == "Debits.Bot.Hosting").ToList())
                {
                    services.Remove(hosted);
                }

                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton(Telegram);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        });

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DebitsDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    public IServiceScope Scope() => Factory.Services.CreateScope();

    /// <summary>Signs synthetic Mini App init data exactly like Telegram does.</summary>
    public string SignInitData(long telegramId, string firstName, string? username = null, string languageCode = "uz", DateTimeOffset? authDate = null, string botToken = BotToken)
    {
        var user = JsonSerializer.Serialize(new { id = telegramId, first_name = firstName, username, language_code = languageCode });
        var fields = new List<(string Key, string Value)>
        {
            ("auth_date", (authDate ?? Clock.GetUtcNow()).ToUnixTimeSeconds().ToString()),
            ("query_id", "AAHdF6IQAAAAAN0XohDhrOrc"),
            ("user", user),
        };
        var hash = Convert.ToHexStringLower(TelegramInitDataValidator.ComputeHash(fields, botToken));
        return string.Join('&', fields.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}")) + "&hash=" + hash;
    }

    public async Task<Session> LoginAsync(string firstName, long? telegramId = null, string languageCode = "uz")
    {
        var id = telegramId ?? NextTelegramId();
        using var anonymous = Factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/telegram", new TelegramAuthRequest(SignInitData(id, firstName, languageCode: languageCode)), Json);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        return new Session(this, auth.User, auth.Token);
    }

    /// <summary>Fresh JWT for the same user, for tests that move the clock beyond the token lifetime.</summary>
    public async Task<HttpClient> ReloginAsync(Session session, Guid shopId) =>
        (await LoginAsync(session.User.FirstName, session.User.TelegramUserId)).In(shopId);

    /// <summary>Owner creates a shop via the API; a second user joins as worker through the invite (accepted bot-side).</summary>
    public async Task<ShopSetup> ShopWithWorkerAsync(string shopName)
    {
        var owner = await LoginAsync("Sarvar");
        var created = await owner.Client.PostAsJsonAsync("/api/shops", new CreateShopRequest(shopName, 30, null), Json);
        created.EnsureSuccessStatusCode();
        var shop = (await created.Content.ReadFromJsonAsync<CreateShopResponse>(Json))!;

        var worker = await LoginAsync("Aziz");
        var inviteResponse = await owner.In(shop.Shop.Id).PostAsJsonAsync("/api/invites", new CreateInviteRequest(null), Json);
        inviteResponse.EnsureSuccessStatusCode();
        var invite = (await inviteResponse.Content.ReadFromJsonAsync<InviteResponse>(Json))!;
        var token = invite.Link[(invite.Link.IndexOf("start=inv_", StringComparison.Ordinal) + "start=inv_".Length)..];

        using var scope = Scope();
        var acceptance = await scope.ServiceProvider.GetRequiredService<MembershipService>().AcceptInviteAsync(token, worker.User.Id);
        acceptance.Should().BeOfType<InviteAcceptance.Joined>();

        return new ShopSetup(shop.Shop.Id, owner, worker);
    }
}

public sealed record ShopSetup(Guid ShopId, Session Owner, Session Worker);

public sealed class Session(ApiFixture fx, MeDto user, string token)
{
    private readonly Dictionary<Guid, HttpClient> _clients = [];
    private HttpClient? _client;

    public MeDto User { get; } = user;
    public string Token { get; } = token;

    /// <summary>Authenticated, no shop header.</summary>
    public HttpClient Client => _client ??= Create(null);

    /// <summary>Authenticated inside <paramref name="shopId"/>.</summary>
    public HttpClient In(Guid shopId)
    {
        if (!_clients.TryGetValue(shopId, out var client))
        {
            client = Create(shopId);
            _clients[shopId] = client;
        }

        return client;
    }

    private HttpClient Create(Guid? shopId)
    {
        var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (shopId is { } id)
        {
            client.DefaultRequestHeaders.Add(TenantMiddleware.ShopHeader, id.ToString());
        }

        return client;
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"expected success but got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, ApiFixture.Json)!;
    }

    public static async Task<string> CodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? string.Empty : string.Empty;
    }

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PatchAsync(url, JsonContent.Create(body, options: ApiFixture.Json));

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsync(url, JsonContent.Create(body, options: ApiFixture.Json));
}
