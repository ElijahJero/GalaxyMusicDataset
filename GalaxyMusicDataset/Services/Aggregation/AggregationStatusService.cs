using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class AggregationStatusService(
    AppDbContext db,
    AggregationProgress progress,
    ApiCallRecorder recorder,
    IOptionsMonitor<LastFmOptions> lastFm,
    IOptionsMonitor<DiscogsOptions> discogs,
    IOptionsMonitor<TheAudioDbOptions> audioDb,
    IOptionsMonitor<AggregationOptions> aggregation)
{
    public async Task<AggregationStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var state = await db.SyncStates.AsNoTracking().FirstAsync(cancellationToken);
        var scrobbles = await db.Scrobbles.CountAsync(cancellationToken);
        var tracks = await db.Tracks.CountAsync(cancellationToken);
        var artists = await db.Artists.CountAsync(cancellationToken);
        var albums = await db.Albums.CountAsync(cancellationToken);
        var tags = await db.Tags.CountAsync(cancellationToken);
        var withMbid = await db.Tracks.CountAsync(t => t.Mbid != null, cancellationToken);
        var withDuration = await db.Tracks.CountAsync(t => t.DurationMs != null, cancellationToken);

        var lookups = await db.TrackLookups
            .AsNoTracking()
            .GroupBy(l => l.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var payloads = await db.TrackSourcePayloads
            .AsNoTracking()
            .GroupBy(p => new { p.Source, p.Status })
            .Select(g => new { g.Key.Source, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var jobs = await db.AggregationJobs
            .AsNoTracking()
            .OrderByDescending(j => j.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        var recentApi = await db.ApiRequestLogs
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(25)
            .ToListAsync(cancellationToken);

        DateTimeOffset? newest = null;
        DateTimeOffset? oldest = null;
        if (state.NewestUnix is not null)
        {
            newest = DateTimeOffset.FromUnixTimeSeconds(state.NewestUnix.Value);
        }

        if (state.OldestUnix is not null)
        {
            oldest = DateTimeOffset.FromUnixTimeSeconds(state.OldestUnix.Value);
        }

        var coverage = tracks == 0 ? 0 : Math.Round(100.0 * withMbid / tracks, 1);
        var playcountGap = state.LastFmPlaycount is null ? null : state.LastFmPlaycount - scrobbles;

        return new AggregationStatusDto
        {
            LastFmConfigured = lastFm.CurrentValue.IsConfigured,
            LastFmUsername = lastFm.CurrentValue.Username,
            DiscogsConfigured = discogs.CurrentValue.IsConfigured,
            TheAudioDbConfigured = audioDb.CurrentValue.IsConfigured,
            EnableMusicBrainz = aggregation.CurrentValue.EnableMusicBrainz,
            EnableLastFmTrackInfo = aggregation.CurrentValue.EnableLastFmTrackInfo,
            EnableDiscogs = aggregation.CurrentValue.EnableDiscogs,
            EnableTheAudioDb = aggregation.CurrentValue.EnableTheAudioDb,
            ScrobbleCount = scrobbles,
            TrackCount = tracks,
            ArtistCount = artists,
            AlbumCount = albums,
            TagCount = tags,
            TracksWithMbid = withMbid,
            TracksWithDuration = withDuration,
            MbidCoveragePercent = coverage,
            LastFmPlaycount = state.LastFmPlaycount,
            PlaycountGap = playcountGap,
            NewestScrobble = newest,
            OldestScrobble = oldest,
            LastSuccessfulSyncUtc = state.LastSuccessfulSyncUtc,
            LastAttemptUtc = state.LastAttemptUtc,
            LastSyncError = state.LastSyncError,
            IsBackfillComplete = state.IsBackfillComplete,
            BackfillCursorDay = state.BackfillCursorDay,
            BackfillDaysCompleted = state.BackfillDaysCompleted,
            AccountRegisteredUtc = state.AccountRegisteredUtc,
            IncrementalRuns = state.IncrementalRuns,
            EnrichmentPaused = state.EnrichmentPaused,
            Lookups = lookups.ToDictionary(x => x.Key.ToString(), x => x.Count),
            SourcePayloads = payloads
                .Select(p => new SourcePayloadCount(p.Source.ToString(), p.Status.ToString(), p.Count))
                .ToList(),
            Jobs = jobs,
            RecentApiCalls = recentApi,
            LiveApiStats = recorder.Snapshot().Values.ToList(),
            Progress = progress
        };
    }
}

public sealed class AggregationStatusDto
{
    public bool LastFmConfigured { get; set; }
    public string? LastFmUsername { get; set; }
    public bool DiscogsConfigured { get; set; }
    public bool TheAudioDbConfigured { get; set; }
    public bool EnableMusicBrainz { get; set; }
    public bool EnableLastFmTrackInfo { get; set; }
    public bool EnableDiscogs { get; set; }
    public bool EnableTheAudioDb { get; set; }
    public int ScrobbleCount { get; set; }
    public int TrackCount { get; set; }
    public int ArtistCount { get; set; }
    public int AlbumCount { get; set; }
    public int TagCount { get; set; }
    public int TracksWithMbid { get; set; }
    public int TracksWithDuration { get; set; }
    public double MbidCoveragePercent { get; set; }
    public long? LastFmPlaycount { get; set; }
    public long? PlaycountGap { get; set; }
    public DateTimeOffset? NewestScrobble { get; set; }
    public DateTimeOffset? OldestScrobble { get; set; }
    public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public string? LastSyncError { get; set; }
    public bool IsBackfillComplete { get; set; }
    public DateTimeOffset? BackfillCursorDay { get; set; }
    public int BackfillDaysCompleted { get; set; }
    public DateTimeOffset? AccountRegisteredUtc { get; set; }
    public int IncrementalRuns { get; set; }
    public bool EnrichmentPaused { get; set; }
    public Dictionary<string, int> Lookups { get; set; } = [];
    public List<SourcePayloadCount> SourcePayloads { get; set; } = [];
    public List<Data.Entities.AggregationJob> Jobs { get; set; } = [];
    public List<Data.Entities.ApiRequestLog> RecentApiCalls { get; set; } = [];
    public List<ApiSourceStats> LiveApiStats { get; set; } = [];
    public AggregationProgress Progress { get; set; } = null!;
}

public sealed record SourcePayloadCount(string Source, string Status, int Count);
