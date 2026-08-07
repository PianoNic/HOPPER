using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.Maintenance
{
    public sealed class BlobReclaimService(
        IServiceScopeFactory scopes,
        IConfiguration configuration,
        ILogger<BlobReclaimService> log) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await SweepAsync(afterRestart: true, stoppingToken);

            var interval = BlobReclaimer.Interval(configuration);
            if (interval <= TimeSpan.Zero)
                return;

            using var timer = new PeriodicTimer(interval);

            while (await WaitAsync(timer, stoppingToken))
                await SweepAsync(afterRestart: false, stoppingToken);
        }

        private async Task SweepAsync(bool afterRestart, CancellationToken stoppingToken)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reclaimer = scope.ServiceProvider.GetRequiredService<BlobReclaimer>();
                await reclaimer.SweepAsync(DateTime.UtcNow, afterRestart, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "The blob reclaim sweep did not complete. It will be retried on the next pass.");
            }
        }

        private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
        {
            try
            {
                return await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
