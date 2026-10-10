using Debits.Bot.Reminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Debits.Bot.Hosting;

/// <summary>Runs <see cref="ReminderService"/> at startup (catching up on today) and then every <see cref="ReminderSchedule.Tick"/>.</summary>
public sealed class ReminderHostedService(IServiceScopeFactory scopes, TimeProvider clock, IOptions<BotOptions> options, ILogger<ReminderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            return;
        }

        try
        {
            // Let BotSetupHostedService and the migrations of a fresh deploy settle.
            await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReminderService>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder run failed");
            }

            try
            {
                await Task.Delay(ReminderSchedule.DelayToNextTick(clock.GetUtcNow().UtcDateTime), clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
