using Debits.Bot.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Debits.Bot.Hosting;

/// <summary>Long-polling loop for development. Updates are processed one by one in arrival order.</summary>
public sealed class PollingHostedService(
    IServiceProvider services,
    UpdateDispatcher dispatcher,
    IOptions<BotOptions> options,
    ILogger<PollingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.IsConfigured || o.UseWebhook)
        {
            return;
        }

        // Let BotSetupHostedService finish (it runs before us, but the webhook removal must be done before getUpdates).
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        var bot = services.GetRequiredService<ITelegramBotClient>();
        logger.LogInformation("Polling started");

        var offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await bot.GetUpdates(offset, limit: 100, timeout: 30, allowedUpdates: BotSetupHostedService.AllowedUpdates, cancellationToken: stoppingToken);
                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    await dispatcher.DispatchAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ApiRequestException ex)
            {
                logger.LogError(ex, "Telegram API error while polling ({ErrorCode})", ex.ErrorCode);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Polling error");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
