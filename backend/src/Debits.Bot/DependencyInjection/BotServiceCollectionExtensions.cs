using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Debits.Bot.DependencyInjection;

public static class BotServiceCollectionExtensions
{
    public static IServiceCollection AddDebitsBot(this IServiceCollection services, IConfiguration configuration)
    {
        // Phase 2: ITelegramBotClient, UpdateDispatcher, polling/webhook, ReminderService (phase 6)
        return services;
    }
}
