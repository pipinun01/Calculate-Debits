using Debits.Application.DependencyInjection;
using Debits.Application.Tenancy;
using Debits.Domain.Entities;
using Debits.Infrastructure.DependencyInjection;
using Debits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Debits.Integration.Tests.Persistence;

/// <summary>One Postgres container per test class; migrations applied once. Each test works in its own DI scope.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("debits_test")
        .WithUsername("debits")
        .WithPassword("debits")
        .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    private long _nextTelegramId = 100_000;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Unique Telegram user id per call; the container is shared by all tests of a class.</summary>
    public long NextTelegramId() => Interlocked.Increment(ref _nextTelegramId);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddDebitsApplication();
        services.AddDebitsInfrastructure(ConnectionString);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DebitsDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Scope with the tenant preset (or no tenant when <paramref name="shopId"/> is null).</summary>
    public TestScope Scope(Guid? shopId = null, Guid? memberId = null, MemberRole? role = null)
    {
        var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(shopId, memberId, null, role, AppLanguage.UzLatn);
        return new TestScope(scope);
    }

    /// <summary>Creates a shop with one owner (user + member) outside any tenant.</summary>
    public async Task<(Shop Shop, ShopMember Owner)> CreateShopAsync(string name)
    {
        using var scope = Scope();
        var db = scope.Db;
        var telegramUserId = NextTelegramId();
        var user = new AppUser { TelegramUserId = telegramUserId, FirstName = $"User {telegramUserId}" };
        var shop = new Shop { Name = name };
        var owner = new ShopMember { Shop = shop, User = user, Role = MemberRole.Owner };
        db.Users.Add(user);
        db.Shops.Add(shop);
        db.ShopMembers.Add(owner);
        await db.SaveChangesAsync();
        return (shop, owner);
    }
}

public sealed class TestScope(IServiceScope scope) : IDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;

    public DebitsDbContext Db => scope.ServiceProvider.GetRequiredService<DebitsDbContext>();

    public T Get<T>() where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose() => scope.Dispose();
}
