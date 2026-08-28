using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class AggregationHostedService(
    IServiceScopeFactory scopes,
    AggregationCoordinator coordinator,
    AggregationProgress progress,
    IOptionsMonitor<AggregationOptions> options,
    IOptionsMonitor<LastFmOptions> lastFmOptions,
    ILogger<AggregationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var boot = scopes.CreateAsyncScope())
        {
            var db = boot.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.SyncStates.FirstAsync(stoppingToken);
            progress.SetEnrichmentPaused(state.EnrichmentPaused);
            await db.TrackLookups
                .Where(l => l.Status == LookupStatus.InProgress)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(l => l.Status, LookupStatus.Pending),
                    stoppingToken);
        }

        progress.Log("Aggregation worker started.");
        if (lastFmOptions.CurrentValue.IsConfigured)
        {
            coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.SyncIncremental));
            coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.Backfill, 3));
        }
        else
        {
            progress.Log("Last.fm is not configured. Set API key and username on Settings.");
        }

        var incrementalTimer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, options.CurrentValue.IncrementalIntervalMinutes)));
        var commandTask = ProcessCommandsAsync(stoppingToken);
        var timerTask = RunTimerAsync(incrementalTimer, stoppingToken);
        var enrichTask = RunEnrichmentLoopAsync(stoppingToken);
        await Task.WhenAll(commandTask, timerTask, enrichTask);
    }

    private async Task RunTimerAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!lastFmOptions.CurrentValue.IsConfigured)
                {
                    continue;
                }

                coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.SyncIncremental));
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var state = await db.SyncStates.FirstAsync(stoppingToken);
                if (!state.IsBackfillComplete)
                {
                    coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.Backfill, 7));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessCommandsAsync(CancellationToken stoppingToken)
    {
        await foreach (var command in coordinator.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<ScrobbleSyncService>();
                var lookups = scope.ServiceProvider.GetRequiredService<MusicBrainzLookupService>();
                switch (command.Kind)
                {
                    case AggregationCommandKind.SyncIncremental:
                        await sync.RefreshUserInfoAsync(stoppingToken);
                        await sync.SyncIncrementalAsync(stoppingToken);
                        break;
                    case AggregationCommandKind.Backfill:
                        await sync.BackfillNextDaysAsync(command.BackfillDays, stoppingToken);
                        break;
                    case AggregationCommandKind.RetryFailedLookups:
                        await lookups.RetryFailedAsync(stoppingToken);
                        progress.Log("Queued failed/not-found lookups for retry.");
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Aggregation command {Kind} failed", command.Kind);
                progress.Error($"{command.Kind} failed: {ex.Message}");
            }
        }
    }

    private async Task RunEnrichmentLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (progress.EnrichmentPaused || progress.SyncRunning)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                await using var scope = scopes.CreateAsyncScope();
                var lookups = scope.ServiceProvider.GetRequiredService<MusicBrainzLookupService>();
                var metadata = scope.ServiceProvider.GetRequiredService<MetadataEnrichmentService>();
                progress.SetEnrichmentRunning(true);

                var worked = await lookups.ProcessNextAsync(stoppingToken);
                if (worked == 0)
                {
                    worked = await metadata.EnrichNextAsync(stoppingToken);
                }

                progress.SetEnrichmentRunning(worked > 0);
                if (worked == 0)
                {
                    if (progress.Phase != "Idle" && !progress.SyncRunning)
                    {
                        progress.SetPhase("Idle");
                        progress.SetCurrentItem(null);
                    }

                    await Task.Delay(2500, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Enrichment loop failed");
                progress.Error("Enrichment loop: " + ex.Message);
                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}
