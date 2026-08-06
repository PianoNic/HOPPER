using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.Imports
{
    /// <summary>Drains the import queue, one job at a time, each in its own DI scope so every import
    /// gets a fresh DbContext rather than accumulating a change tracker across a 340-file pack.
    ///
    /// Nothing may escape this loop: an unhandled exception here would take the host down and leave
    /// every queued import unreachable, so a failed job is recorded on its own row (PackImporter does
    /// that in its finally) and the worker moves to the next one.</summary>
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
