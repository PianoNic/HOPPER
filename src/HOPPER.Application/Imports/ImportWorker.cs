using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.Imports
{
    public class ImportWorker(IImportQueue queue, IServiceScopeFactory scopeFactory, ILogger<ImportWorker> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var importId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var importer = scope.ServiceProvider.GetRequiredService<IPackImporter>();
                    await importer.RunAsync(importId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The import worker could not run import {ImportId}", importId);
                }
            }
        }
    }
}
