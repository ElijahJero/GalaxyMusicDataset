using System.Threading.Channels;
using GalaxyMusicDataset.Data;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public enum AggregationCommandKind
{
    SyncIncremental,
    Backfill,
    RetryFailedLookups,
    ReconcileRecent,
    ReconcileFull
}

public sealed record AggregationCommand(AggregationCommandKind Kind, int BackfillDays = 14);

public sealed class AggregationCoordinator
{
    private readonly Channel<AggregationCommand> _commands = Channel.CreateUnbounded<AggregationCommand>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<AggregationCommand> Reader => _commands.Reader;

    public bool TryEnqueue(AggregationCommand command) => _commands.Writer.TryWrite(command);

    public async Task SetPausedAsync(IServiceScopeFactory scopes, bool paused, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var progress = scope.ServiceProvider.GetRequiredService<AggregationProgress>();
        var state = await db.GetSyncStateAsync(cancellationToken);
        state.EnrichmentPaused = paused;
        await db.SaveChangesAsync(cancellationToken);
        progress.SetEnrichmentPaused(paused);
        progress.Log(paused ? "Enrichment paused." : "Enrichment resumed.");
    }

    public async Task SetDeletionCheckPausedAsync(IServiceScopeFactory scopes, bool paused, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var progress = scope.ServiceProvider.GetRequiredService<AggregationProgress>();
        var state = await db.GetSyncStateAsync(cancellationToken);
        state.DeletionCheckPaused = paused;
        await db.SaveChangesAsync(cancellationToken);
        progress.Log(paused
            ? "Nightly 7-day Last.fm check paused."
            : "Nightly 7-day Last.fm check resumed.");
        if (!paused && ScrobbleReconcile.NightlyCheckDue(state.LastDeletionCheckUtc, DateTimeOffset.UtcNow, paused: false))
        {
            TryEnqueue(new AggregationCommand(AggregationCommandKind.ReconcileRecent));
        }
    }
}
