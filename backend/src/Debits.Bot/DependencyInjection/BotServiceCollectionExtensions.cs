using Debits.Bot.Conversation;
using Debits.Bot.Dispatch;
using Debits.Bot.Entries;
using Debits.Bot.Handlers;
using Debits.Bot.Hosting;
using Debits.Bot.Localization;
using Debits.Bot.Reminders;
using Debits.Bot.Reports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Debits.Bot.DependencyInjection;

public static class BotServiceCollectionExtensions
{
    /// <summary>Full bot: options, Telegram client, handlers and hosted services (setup + polling).</summary>
    public static IServiceCollection AddDebitsBot(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.Section));
        services.Configure<MiniAppOptions>(configuration.GetSection(MiniAppOptions.Section));
        services.AddDebitsBotCore();

        // No HttpClient logging: request URLs contain the bot token.
        services.AddHttpClient(nameof(TelegramBotClient)).RemoveAllLoggers();
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<BotOptions>>().Value;
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TelegramBotClient));
            return new TelegramBotClient(o.Token, http);
        });

        services.AddHostedService<BotSetupHostedService>();
        services.AddHostedService<PollingHostedService>();
        services.AddHostedService<PendingEntrySweepHostedService>();
        services.AddHostedService<ReminderHostedService>();
        return services;
    }

    /// <summary>Handlers, dispatcher and strings only; the caller supplies <see cref="ITelegramBotClient"/> and options (used by tests).</summary>
    public static IServiceCollection AddDebitsBotCore(this IServiceCollection services)
    {
        services.AddSingleton<BotStrings>();
        services.AddSingleton<BotIdentity>(sp => new BotIdentity { Username = sp.GetRequiredService<IOptions<BotOptions>>().Value.Username });
        services.AddSingleton<UpdateDispatcher>();

        services.AddScoped<UpdateRouter>();
        services.AddScoped<ConversationStore>();
        services.AddScoped<StartHandler>();
        services.AddScoped<MenuHandler>();
        services.AddScoped<ShopsHandler>();
        services.AddScoped<LanguageHandler>();
        services.AddScoped<InviteHandler>();
        services.AddScoped<EntryHandler>();
        services.AddScoped<VoiceHandler>();
        services.AddScoped<PendingEntrySweeper>();
        services.AddScoped<ReminderService>();
        services.AddScoped<ReportSender>();
        return services;
    }
}
