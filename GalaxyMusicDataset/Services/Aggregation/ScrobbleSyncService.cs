using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Scrobbling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class ScrobbleSyncService(
    AppDbContext db,
    ScrobbleIngestService ingest,
    ScrobbleReconcileService reconcile,
    CatalogService catalog,
    ExternalClientFactory clients,
    AggregationProgress progress,
    IOptionsMonitor<AggregationOptions> aggregationOptions)
{
    public async Task<AggregationJob> SyncIncrementalAsync(CancellationToken cancellationToken)
    {
        var client = RequireClient();
        var job = await StartJobAsync(JobKind.ScrobbleIncremental, cancellationToken);
        progress.SetSyncRunning(true);
        progress.SetPhase($"{client.DisplayName} incremental sync");
        try
        {
            var state = await GetStateAsync(cancellationToken);
            var overlap = aggregationOptions.CurrentValue.OverlapSeconds;
            long? from = state.NewestUnix is null ? null : state.NewestUnix.Value - overlap;
            var to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            progress.Log(from is null
                ? $"Incremental sync ({client.DisplayName}): fetching latest window (no watermark yet)."
                : $"Incremental sync ({client.DisplayName}): from={from} to={to}.");

            var result = await PullWindowAsync(client, from, to, job, cancellationToken);
            await UpdateWatermarksAsync(state, result.Plays, cancellationToken);
            state.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
            state.LastAttemptUtc = DateTimeOffset.UtcNow;
            state.LastSyncError = result.Warning;
            state.IncrementalRuns++;
            state.LastFmUsername = client.DisplayName;
            await db.SaveChangesAsync(cancellationToken);

            job.Status = result.Ok ? JobStatus.Succeeded : JobStatus.Partial;
            job.Message = result.Warning ?? $"Inserted {job.ItemsSucceeded}, skipped {job.ItemsSkipped}.";
            return await FinishJobAsync(job, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress.Error(ex.Message);
            job.Status = JobStatus.Failed;
            job.Message = ex.Message;
            catalog.DiscardConflictingCatalogInserts();
            var state = await GetStateAsync(cancellationToken);
            state.LastAttemptUtc = DateTimeOffset.UtcNow;
            state.LastSyncError = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return await FinishJobAsync(job, cancellationToken);
        }
        finally
        {
            progress.SetSyncRunning(false);
            progress.SetPhase("Idle");
            progress.SetCurrentItem(null);
        }
    }

    public async Task<AggregationJob> BackfillNextDaysAsync(int maxDays, CancellationToken cancellationToken)
    {
        var client = RequireClient();
        var job = await StartJobAsync(JobKind.ScrobbleBackfill, cancellationToken);
        progress.SetSyncRunning(true);
        progress.SetPhase($"{client.DisplayName} history backfill");
        try
        {
            var state = await GetStateAsync(cancellationToken);
            if (state.IsBackfillComplete)
            {
                job.Status = JobStatus.Succeeded;
                job.Message = "Backfill already complete.";
                job.ItemsSkipped = 1;
                return await FinishJobAsync(job, cancellationToken);
            }

            await EnsureUserBoundsAsync(client, state, cancellationToken);

            var daysDone = 0;
            var emptyStreak = 0;
            while (daysDone < maxDays && !state.IsBackfillComplete && !cancellationToken.IsCancellationRequested)
            {
                var day = state.BackfillCursorDay?.UtcDateTime.Date
                          ?? DateTime.UtcNow.Date;
                var registered = state.AccountRegisteredUtc?.UtcDateTime.Date
                                 ?? day.AddYears(-20);
                if (day < registered)
                {
                    state.IsBackfillComplete = true;
                    progress.Log("Backfill reached account registration date.");
                    break;
                }

                var dayStart = new DateTimeOffset(day, TimeSpan.Zero);
                var from = dayStart.ToUnixTimeSeconds() - 1;
                var to = dayStart.AddDays(1).ToUnixTimeSeconds();
                progress.SetBackfill(day.ToString("yyyy-MM-dd"), state.BackfillDaysCompleted);
                progress.SetCurrentItem($"Backfill {day:yyyy-MM-dd}");
                progress.Log($"Backfilling {day:yyyy-MM-dd} via {client.DisplayName}.");

                var window = await PullWindowAsync(client, from, to, job, cancellationToken);
                await UpdateWatermarksAsync(state, window.Plays, cancellationToken);
                state.BackfillCursorDay = dayStart.AddDays(-1);
                state.BackfillDaysCompleted++;
                state.LastAttemptUtc = DateTimeOffset.UtcNow;
                if (!string.IsNullOrWhiteSpace(window.Warning))
                {
                    state.LastSyncError = window.Warning;
                    job.Status = JobStatus.Partial;
                }

                await db.SaveChangesAsync(cancellationToken);
                daysDone++;

                var dated = window.Plays.Count(t => t.TimestampUnix is not null && !t.IsNowPlaying);
                emptyStreak = dated == 0 ? emptyStreak + 1 : 0;
                if (emptyStreak >= 14 && day < DateTime.UtcNow.Date.AddDays(-30))
                {
                    state.IsBackfillComplete = true;
                    progress.Log("Backfill stopping after 14 consecutive empty days.");
                    break;
                }
            }

            if (state.BackfillCursorDay is { } cursor &&
                state.AccountRegisteredUtc is { } registeredUtc &&
                cursor < registeredUtc)
            {
                state.IsBackfillComplete = true;
            }

            if (job.Status == JobStatus.Running)
            {
                job.Status = JobStatus.Succeeded;
            }

            job.Message = state.IsBackfillComplete
                ? "Backfill complete."
                : $"Backfilled {daysDone} day(s); cursor={state.BackfillCursorDay:yyyy-MM-dd}.";
            await db.SaveChangesAsync(cancellationToken);
            return await FinishJobAsync(job, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress.Error(ex.Message);
            job.Status = JobStatus.Failed;
            job.Message = ex.Message;
            catalog.DiscardConflictingCatalogInserts();
            var state = await GetStateAsync(cancellationToken);
            state.LastAttemptUtc = DateTimeOffset.UtcNow;
            state.LastSyncError = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return await FinishJobAsync(job, cancellationToken);
        }
        finally
        {
            progress.SetSyncRunning(false);
            progress.SetPhase("Idle");
            progress.SetCurrentItem(null);
        }
    }

    public Task<AggregationJob> ReconcileRecentAsync(CancellationToken cancellationToken) =>
        ReconcileAsync(fullHistory: false, cancellationToken);

    public Task<AggregationJob> ReconcileFullAsync(CancellationToken cancellationToken) =>
        ReconcileAsync(fullHistory: true, cancellationToken);

    public async Task RefreshUserInfoAsync(CancellationToken cancellationToken)
    {
        var client = clients.TryCreateScrobbleClient();
        if (client is null)
        {
            return;
        }

        var info = await client.GetUserInfoAsync(cancellationToken);
        var state = await GetStateAsync(cancellationToken);
        state.LastFmPlaycount = info.Playcount;
        if (info.RegisteredUtc is not null)
        {
            state.AccountRegisteredUtc = info.RegisteredUtc;
        }

        state.LastFmUsername = info.Name;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AggregationJob> ReconcileAsync(bool fullHistory, CancellationToken cancellationToken)
    {
        var client = RequireClient();
        var job = await StartJobAsync(JobKind.ScrobbleReconcile, cancellationToken);
        progress.SetSyncRunning(true);
        progress.SetPhase(fullHistory ? $"{client.DisplayName} full resync" : $"{client.DisplayName} 7-day check");
        try
        {
            var state = await GetStateAsync(cancellationToken);
            var today = DateTime.UtcNow.Date;
            DateTime oldest;
            if (fullHistory)
            {
                await EnsureUserBoundsAsync(client, state, cancellationToken);
                oldest = state.AccountRegisteredUtc?.UtcDateTime.Date ?? today.AddYears(-20);
                if (state.AccountRegisteredUtc is null)
                {
                    progress.Log($"{client.DisplayName} did not return a registration date. Full resync will walk back 20 years.");
                }
            }
            else
            {
                oldest = today.AddDays(-(ScrobbleReconcile.NightlyDays - 1));
            }

            if (oldest > today)
            {
                oldest = today;
            }

            var removed = 0;
            var incomplete = 0;
            var daysDone = 0;
            var pageSize = PageSize();
            var allowDeletes = client.SupportsDeletionReconcile;
            if (!allowDeletes)
            {
                progress.Log($"{client.DisplayName} does not support deletion reconcile; missing plays will still be inserted.");
            }

            for (var day = today; day >= oldest && !cancellationToken.IsCancellationRequested; day = day.AddDays(-1))
            {
                var (fromInclusive, toExclusive) = ScrobbleReconcile.UtcDayRange(day);
                var (apiFrom, apiTo) = ScrobbleReconcile.LastFmWindow(fromInclusive, toExclusive);
                progress.SetCurrentItem($"Reconcile {day:yyyy-MM-dd}");
                progress.Log($"Reconciling {day:yyyy-MM-dd} via {client.DisplayName}.");

                var window = await client.GetPlaysWindowAsync(apiFrom, apiTo, pageSize, cancellationToken);
                if (!window.Ok && window.Warning is not null)
                {
                    progress.Log(window.Warning);
                }

                var dayResult = await reconcile.ApplyWindowAsync(
                    fromInclusive,
                    toExclusive,
                    window,
                    client.Provider,
                    allowDeletes,
                    cancellationToken);
                await UpdateWatermarksAsync(state, window.Plays, cancellationToken);
                job.ItemsProcessed += window.Plays.Count;
                job.ItemsSucceeded += dayResult.Inserted;
                job.ItemsSkipped += dayResult.Duplicates + dayResult.Skipped;
                removed += dayResult.Removed;
                if (dayResult.DeletesSkipped && allowDeletes)
                {
                    incomplete++;
                }

                daysDone++;
                progress.SetJobCounts(job.ItemsProcessed, job.ItemsSucceeded, incomplete);
                state.LastAttemptUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            state.LastDeletionCheckUtc = DateTimeOffset.UtcNow;
            state.LastFmUsername = client.DisplayName;
            job.Status = incomplete == 0 ? JobStatus.Succeeded : JobStatus.Partial;
            job.Message = incomplete == 0
                ? $"Reconciled {daysDone} day(s): +{job.ItemsSucceeded} missing, -{removed} deleted."
                : $"Reconciled {daysDone} day(s): +{job.ItemsSucceeded} missing, -{removed} deleted. Skipped deletes on {incomplete} incomplete day(s).";
            progress.Log(job.Message);
            await db.SaveChangesAsync(cancellationToken);
            return await FinishJobAsync(job, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress.Error(ex.Message);
            job.Status = JobStatus.Failed;
            job.Message = ex.Message;
            catalog.DiscardConflictingCatalogInserts();
            var state = await GetStateAsync(cancellationToken);
            state.LastAttemptUtc = DateTimeOffset.UtcNow;
            state.LastSyncError = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return await FinishJobAsync(job, cancellationToken);
        }
        finally
        {
            progress.SetSyncRunning(false);
            progress.SetPhase("Idle");
            progress.SetCurrentItem(null);
        }
    }

    private IScrobbleHistoryClient RequireClient()
    {
        var primary = aggregationOptions.CurrentValue.ResolvedPrimaryScrobbleSource;
        return clients.TryCreateScrobbleClient(primary)
               ?? throw new InvalidOperationException(
                   $"Primary scrobble source '{primary}' is not configured. Set it on Settings.");
    }

    private int PageSize()
    {
        var agg = aggregationOptions.CurrentValue;
        return Math.Max(1, agg.ScrobblePageSize > 0 ? agg.ScrobblePageSize : agg.LastFmPageSize);
    }

    private async Task EnsureUserBoundsAsync(
        IScrobbleHistoryClient client,
        SyncState state,
        CancellationToken cancellationToken)
    {
        if (state.AccountRegisteredUtc is not null && state.LastFmPlaycount is not null)
        {
            if (state.BackfillCursorDay is null)
            {
                state.BackfillCursorDay = DateTimeOffset.UtcNow.Date;
            }

            return;
        }

        progress.Log($"Fetching {client.DisplayName} user info for playcount and registration date.");
        var info = await client.GetUserInfoAsync(cancellationToken);
        state.LastFmPlaycount = info.Playcount;
        if (info.RegisteredUtc is not null)
        {
            state.AccountRegisteredUtc = info.RegisteredUtc;
        }

        state.LastFmUsername = info.Name;
        state.BackfillCursorDay ??= DateTimeOffset.UtcNow.Date;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<PlayWindowResult> PullWindowAsync(
        IScrobbleHistoryClient client,
        long? from,
        long? to,
        AggregationJob job,
        CancellationToken cancellationToken)
    {
        var window = await client.GetPlaysWindowAsync(from, to, PageSize(), cancellationToken);
        if (!window.Ok && window.Warning is not null)
        {
            progress.Log(window.Warning);
        }

        var ingestResult = await ingest.IngestAsync(window.Plays, cancellationToken);
        job.ItemsProcessed += window.Plays.Count;
        job.ItemsSucceeded += ingestResult.Inserted;
        job.ItemsSkipped += ingestResult.Duplicates + ingestResult.Skipped;
        progress.SetJobCounts(job.ItemsProcessed, job.ItemsSucceeded, job.ItemsFailed);
        progress.Log($"Window ingested: +{ingestResult.Inserted} new, {ingestResult.Duplicates} duplicate, {ingestResult.Skipped} skipped.");
        return window;
    }

    private async Task UpdateWatermarksAsync(
        SyncState state,
        IReadOnlyList<IncomingPlay> tracks,
        CancellationToken cancellationToken)
    {
        var unix = tracks
            .Where(t => t.TimestampUnix is > 0 && !t.IsNowPlaying)
            .Select(t => t.TimestampUnix!.Value)
            .ToList();
        if (unix.Count == 0)
        {
            return;
        }

        var newest = unix.Max();
        var oldest = unix.Min();
        if (state.NewestUnix is null || newest > state.NewestUnix)
        {
            state.NewestUnix = newest;
        }

        if (state.OldestUnix is null || oldest < state.OldestUnix)
        {
            state.OldestUnix = oldest;
        }

        await Task.CompletedTask;
    }

    private async Task<SyncState> GetStateAsync(CancellationToken cancellationToken)
    {
        var state = await db.GetSyncStateAsync(cancellationToken);
        return state;
    }

    private async Task<AggregationJob> StartJobAsync(JobKind kind, CancellationToken cancellationToken)
    {
        var job = new AggregationJob
        {
            Kind = kind,
            Status = JobStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        db.AggregationJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        progress.SetJobCounts(0, 0, 0);
        return job;
    }

    private async Task<AggregationJob> FinishJobAsync(AggregationJob job, CancellationToken cancellationToken)
    {
        job.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        progress.Log($"{job.Kind}: {job.Status} — {job.Message}");
        return job;
    }
}
