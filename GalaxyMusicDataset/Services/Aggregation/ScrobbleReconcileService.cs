using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.LastFm;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed record ReconcileDayResult(int Inserted, int Duplicates, int Skipped, int Removed, bool DeletesSkipped);

public sealed class ScrobbleReconcileService(AppDbContext db, ScrobbleIngestService ingest)
{
    public async Task<ReconcileDayResult> ApplyWindowAsync(
        long dayFromInclusive,
        long dayToExclusive,
        LastFmWindowResult window,
        CancellationToken cancellationToken)
    {
        var ingestResult = await ingest.IngestAsync(window.Tracks, cancellationToken);
        if (!window.Ok)
        {
            return new ReconcileDayResult(ingestResult.Inserted, ingestResult.Duplicates, ingestResult.Skipped, 0, DeletesSkipped: true);
        }

        var remote = window.Tracks
            .Where(t => !t.IsNowPlaying && t.TimestampUnix is > 0)
            .Select(t => t.TimestampUnix!.Value)
            .ToHashSet();

        var local = await db.Scrobbles
            .Where(s => s.UnixTimestamp >= dayFromInclusive && s.UnixTimestamp < dayToExclusive)
            .Select(s => s.UnixTimestamp)
            .ToListAsync(cancellationToken);

        var stale = local.Where(t => !remote.Contains(t)).ToList();
        if (stale.Count == 0)
        {
            return new ReconcileDayResult(ingestResult.Inserted, ingestResult.Duplicates, ingestResult.Skipped, 0, false);
        }

        var removed = await db.Scrobbles
            .Where(s => stale.Contains(s.UnixTimestamp))
            .ExecuteDeleteAsync(cancellationToken);
        return new ReconcileDayResult(ingestResult.Inserted, ingestResult.Duplicates, ingestResult.Skipped, removed, false);
    }
}
