using Debits.Bot.Entries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Debits.Bot.Hosting;

/// <summary>Runs <see cref="PendingEntrySweeper"/> at startup and then hourly.</summary>
public sealed class PendingEntrySweepHostedService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PendingEntrySweepHostedService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var (expired, deleted) = await scope.ServiceProvider.GetRequiredService<PendingEntrySweeper>().SweepAsync(stoppingToken);
                if (expired + deleted > 0)
                {
                    logger.LogInformation("Pending entries sweep: {Expired} expired, {Deleted} deleted", expired, deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Pending entries sweep failed");
            }

            try
            {
                await Task.Delay(Interval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
