using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.Scrobbling;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed record ReconcileDayResult(int Inserted, int Duplicates, int Skipped, int Removed, bool DeletesSkipped);

public sealed class ScrobbleReconcileService(AppDbContext db, ScrobbleIngestService ingest)
{
    public Task<ReconcileDayResult> ApplyWindowAsync(
        long dayFromInclusive,
        long dayToExclusive,
        LastFmWindowResult window,
        CancellationToken cancellationToken) =>
        ApplyWindowAsync(
            dayFromInclusive,
            dayToExclusive,
            new PlayWindowResult(
                IncomingPlayMapper.FromLastFm(window.Tracks).ToList(),
                window.ReportedTotal,
                window.PageCount,
                window.Ok,
                window.Warning),
            ScrobbleProvider.LastFm,
            allowDeletes: true,
            cancellationToken);

    public async Task<ReconcileDayResult> ApplyWindowAsync(
        long dayFromInclusive,
        long dayToExclusive,
        PlayWindowResult window,
        ScrobbleProvider source,
        bool allowDeletes,
        CancellationToken cancellationToken)
    {
        var ingestResult = await ingest.IngestAsync(window.Plays, cancellationToken);
        if (!allowDeletes || !window.Ok)
        {
            return new ReconcileDayResult(
                ingestResult.Inserted,
                ingestResult.Duplicates,
                ingestResult.Skipped,
                0,
                DeletesSkipped: !window.Ok || !allowDeletes);
        }

        var remote = window.Plays
            .Where(t => !t.IsNowPlaying && t.TimestampUnix is > 0)
            .Select(t => t.TimestampUnix!.Value)
            .ToHashSet();

        // Only remove plays that originated from this provider so switching
        // Last.fm → Maloja (with imported history) cannot wipe the warehouse.
        var local = await db.Scrobbles
            .Where(s =>
                s.Source == source &&
                s.UnixTimestamp >= dayFromInclusive &&
                s.UnixTimestamp < dayToExclusive)
            .Select(s => s.UnixTimestamp)
            .ToListAsync(cancellationToken);

        var stale = local.Where(t => !remote.Contains(t)).ToList();
        if (stale.Count == 0)
        {
            return new ReconcileDayResult(ingestResult.Inserted, ingestResult.Duplicates, ingestResult.Skipped, 0, false);
        }

        var removed = await db.Scrobbles
            .Where(s => s.Source == source && stale.Contains(s.UnixTimestamp))
            .ExecuteDeleteAsync(cancellationToken);
        return new ReconcileDayResult(ingestResult.Inserted, ingestResult.Duplicates, ingestResult.Skipped, removed, false);
    }
}
