using Debits.Application.DependencyInjection;
using Debits.Bot;
using Debits.Bot.DependencyInjection;
using Debits.Bot.Dispatch;
using Debits.Infrastructure.DependencyInjection;
using Debits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Testcontainers.PostgreSql;

namespace Debits.Integration.Tests.Bot;

/// <summary>Postgres + the bot's DI graph with a recording fake <see cref="ITelegramBotClient"/>.</summary>
public sealed class BotFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("debits_bot")
        .WithUsername("debits")
        .WithPassword("debits")
        .Build();

    private int _updateId;

    public ServiceProvider Services { get; private set; } = null!;
    public ITelegramBotClient Client { get; } = Substitute.For<ITelegramBotClient>();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));
    public UpdateDispatcher Dispatcher => Services.GetRequiredService<UpdateDispatcher>();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Client.SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<CancellationToken>()).Returns(new Message());
        Client.SendRequest(Arg.Any<IRequest<bool>>(), Arg.Any<CancellationToken>()).Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddDebitsApplication();
        services.AddDebitsInfrastructure(_container.GetConnectionString());
        services.Configure<BotOptions>(o => o.Username = "DebitsTestBot");
        services.Configure<MiniAppOptions>(o => o.Url = "https://app.example.com");
        services.AddDebitsBotCore();
        services.AddSingleton(Client);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DebitsDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }

    public DebitsDbContext NewDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<DebitsDbContext>();

    public Task SendTextAsync(TelegramUser from, string text) => Dispatcher.DispatchAsync(new Update
    {
        Id = Interlocked.Increment(ref _updateId),
        Message = new Message
        {
            Id = _updateId,
            Text = text,
            From = from.ToTelegram(),
            Chat = new Chat { Id = from.Id, Type = ChatType.Private },
        },
    }, CancellationToken.None);

    public Task PressAsync(TelegramUser from, string data) => Dispatcher.DispatchAsync(new Update
    {
        Id = Interlocked.Increment(ref _updateId),
        CallbackQuery = new CallbackQuery
        {
            Id = _updateId.ToString(),
            Data = data,
            From = from.ToTelegram(),
            Message = new Message { Id = _updateId, Chat = new Chat { Id = from.Id, Type = ChatType.Private } },
        },
    }, CancellationToken.None);

    /// <summary>Texts sent to a chat, in order, since the last <see cref="Reset"/>.</summary>
    public IReadOnlyList<SendMessageRequest> SentTo(long chatId) => Client.ReceivedCalls()
        .Select(c => c.GetArguments().FirstOrDefault())
        .OfType<SendMessageRequest>()
        .Where(r => r.ChatId.Identifier == chatId)
        .ToList();

    public IReadOnlyList<EditMessageTextRequest> EditsIn(long chatId) => Client.ReceivedCalls()
        .Select(c => c.GetArguments().FirstOrDefault())
        .OfType<EditMessageTextRequest>()
        .Where(r => r.ChatId.Identifier == chatId)
        .ToList();

    public string LastTextTo(long chatId)
    {
        var all = Client.ReceivedCalls()
            .Select(c => c.GetArguments().FirstOrDefault())
            .Select(a => a switch
            {
                SendMessageRequest s when s.ChatId.Identifier == chatId => s.Text,
                EditMessageTextRequest e when e.ChatId.Identifier == chatId => e.Text,
                _ => null,
            })
            .Where(t => t is not null)
            .ToList();
        return all.LastOrDefault() ?? string.Empty;
    }

    public void Reset() => Client.ClearReceivedCalls();
}

public sealed record TelegramUser(long Id, string FirstName, string? LanguageCode = "uz", string? Username = null)
{
    public User ToTelegram() => new() { Id = Id, FirstName = FirstName, LanguageCode = LanguageCode, Username = Username, IsBot = false };
}
