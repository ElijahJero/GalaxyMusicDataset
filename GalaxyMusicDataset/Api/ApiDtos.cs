using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;

namespace GalaxyMusicDataset.Api;

public sealed record ApiError(string Error);

public sealed record ApiTimeWindow(string Preset, DateTimeOffset From, DateTimeOffset To, string? Q);

public sealed record ApiMeResponse(string Id, string Name, string Prefix, IReadOnlyList<string> Scopes);

public sealed record ApiCatalogResponse(
    string Name,
    string Version,
    string Documentation,
    string Authentication,
    IReadOnlyList<string> Scopes,
    IReadOnlyDictionary<string, string> Endpoints);

public sealed record CreateApiKeyRequest(string? Name, bool Read = true, bool Write = true);

public sealed record CreatedApiKeyResponse(
    string Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    string Token,
    string Warning);

public sealed record AcceptReviewRequest(string? Mbid);

public sealed record BackfillRequest(int Days = 14);

public sealed record JobQueuedResponse(bool Queued, string Command);

public sealed record EnrichTrackResponse(long TrackId, string Message);

public sealed record LibraryTrackDto(
    long Id,
    string Title,
    long ArtistId,
    string ArtistName,
    IReadOnlyList<string> ArtistAliases,
    long? AlbumId,
    string? AlbumTitle,
    string? CoverUrl,
    string? Mbid,
    string? VocaDbSongId,
    string? UtaiteDbSongId,
    string? TouhouDbSongId,
    int? DurationMs,
    string Fingerprint,
    int PlayCount,
    DateTimeOffset? LastPlayedAt,
    string? LookupStatus,
    double? LookupScore,
    string? LookupError,
    IReadOnlyList<string> Tags,
    IReadOnlyList<SourcePayloadInfo> Sources,
    AudioProfileView? Audio,
    string YoutubeMusicUrl)
{
    public bool HasAudio => Audio is not null;
}

public sealed record LookupDto(
    long Id,
    string Fingerprint,
    long? TrackId,
    string ArtistName,
    string TrackName,
    string? AlbumName,
    LookupStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptUtc,
    int AttemptCount,
    string? MatchedMbid,
    double? BestScore,
    string? ErrorMessage,
    string? QueryUsed);

public sealed record ReviewItemDto(
    LookupDto Lookup,
    IReadOnlyList<GalaxyMusicDataset.Services.MusicBrainz.RecordingCandidate> Candidates);

public sealed record AggregationSettingsResponse(
    string? LastFmUsername,
    bool LastFmKeySaved,
    bool DiscogsTokenSaved,
    bool TheAudioDbKeySaved,
    string? MusicBrainzContact,
    string? MusicBrainzBaseUrl,
    string? MusicBrainzCoverArtBaseUrl,
    int? MusicBrainzMinIntervalMs,
    bool EnableMusicBrainz,
    bool EnableLastFmTrackInfo,
    bool EnableDiscogs,
    bool EnableTheAudioDb,
    bool EnableVocaDb,
    bool EnableUtaiteDb,
    bool EnableTouhouDb,
    int IncrementalIntervalMinutes,
    bool SeedSampleData);

public sealed record ProgressSnapshotDto(
    string Phase,
    string? CurrentItem,
    string? LastError,
    DateTimeOffset? LastUpdatedUtc,
    bool SyncRunning,
    bool EnrichmentRunning,
    bool EnrichmentPaused,
    string? BackfillDay,
    int BackfillDaysCompleted,
    int CurrentJobProcessed,
    int CurrentJobSucceeded,
    int CurrentJobFailed,
    IReadOnlyList<string> RecentLog);

public sealed record JobDto(
    long Id,
    string Kind,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ItemsProcessed,
    int ItemsSucceeded,
    int ItemsFailed,
    int ItemsSkipped,
    string? Message);

public sealed record ExternalApiCallDto(
    long Id,
    string Source,
    string Method,
    string Url,
    int? StatusCode,
    bool Success,
    int DurationMs,
    string? Error,
    DateTimeOffset At);

public sealed record LiveApiStatsDto(
    string Source,
    long TotalRequests,
    long Successes,
    long Failures,
    int? LastStatusCode,
    DateTimeOffset? LastCallUtc,
    string? LastError,
    int LastDurationMs,
    long TotalDurationMs);

public sealed record StatusResponse(
    bool LastFmConfigured,
    string? LastFmUsername,
    bool DiscogsConfigured,
    bool TheAudioDbConfigured,
    bool EnableMusicBrainz,
    string? MusicBrainzBaseUrl,
    bool MusicBrainzUsesPublicApi,
    int MusicBrainzIntervalMs,
    bool EnableLastFmTrackInfo,
    bool EnableDiscogs,
    bool EnableTheAudioDb,
    bool EnableVocaDb,
    bool EnableUtaiteDb,
    bool EnableTouhouDb,
    int ScrobbleCount,
    int TrackCount,
    int ArtistCount,
    int AlbumCount,
    int TagCount,
    int TracksWithMbid,
    int TracksWithDuration,
    int TracksWithTags,
    int TracksWithAudio,
    double MbidCoveragePercent,
    IReadOnlyList<SourceCoverage> Coverage,
    long? LastFmPlaycount,
    long? PlaycountGap,
    DateTimeOffset? NewestScrobble,
    DateTimeOffset? OldestScrobble,
    DateTimeOffset? LastSuccessfulSyncUtc,
    DateTimeOffset? LastAttemptUtc,
    string? LastSyncError,
    bool IsBackfillComplete,
    DateTimeOffset? BackfillCursorDay,
    int BackfillDaysCompleted,
    DateTimeOffset? AccountRegisteredUtc,
    int IncrementalRuns,
    bool EnrichmentPaused,
    Dictionary<string, int> Lookups,
    IReadOnlyList<SourcePayloadCount> SourcePayloads,
    IReadOnlyList<JobDto> Jobs,
    IReadOnlyList<ExternalApiCallDto> RecentApiCalls,
    IReadOnlyList<LiveApiStatsDto> LiveApiStats,
    ProgressSnapshotDto Progress);

public static class ApiMapping
{
    public static ApiTimeWindow Window(TimeRange range, string? q) =>
        new(range.Preset, range.From, range.To, string.IsNullOrWhiteSpace(q) ? null : q.Trim());

    public static StatusResponse Status(AggregationStatusDto s)
    {
        var p = s.Progress;
        return new StatusResponse(
            s.LastFmConfigured,
            s.LastFmUsername,
            s.DiscogsConfigured,
            s.TheAudioDbConfigured,
            s.EnableMusicBrainz,
            s.MusicBrainzBaseUrl,
            s.MusicBrainzUsesPublicApi,
            s.MusicBrainzIntervalMs,
            s.EnableLastFmTrackInfo,
            s.EnableDiscogs,
            s.EnableTheAudioDb,
            s.EnableVocaDb,
            s.EnableUtaiteDb,
            s.EnableTouhouDb,
            s.ScrobbleCount,
            s.TrackCount,
            s.ArtistCount,
            s.AlbumCount,
            s.TagCount,
            s.TracksWithMbid,
            s.TracksWithDuration,
            s.TracksWithTags,
            s.TracksWithAudio,
            s.MbidCoveragePercent,
            s.Coverage,
            s.LastFmPlaycount,
            s.PlaycountGap,
            s.NewestScrobble,
            s.OldestScrobble,
            s.LastSuccessfulSyncUtc,
            s.LastAttemptUtc,
            s.LastSyncError,
            s.IsBackfillComplete,
            s.BackfillCursorDay,
            s.BackfillDaysCompleted,
            s.AccountRegisteredUtc,
            s.IncrementalRuns,
            s.EnrichmentPaused,
            s.Lookups,
            s.SourcePayloads,
            s.Jobs.Select(j => new JobDto(
                j.Id, j.Kind.ToString(), j.Status.ToString(), j.StartedAt, j.FinishedAt,
                j.ItemsProcessed, j.ItemsSucceeded, j.ItemsFailed, j.ItemsSkipped, j.Message)).ToList(),
            s.RecentApiCalls.Select(c => new ExternalApiCallDto(
                c.Id, c.Source, c.Method, c.Url, c.StatusCode, c.Success, c.DurationMs, c.Error, c.At)).ToList(),
            s.LiveApiStats.Select(x => new LiveApiStatsDto(
                x.Source, x.TotalRequests, x.Successes, x.Failures, x.LastStatusCode,
                x.LastCallUtc, x.LastError, x.LastDurationMs, x.TotalDurationMs)).ToList(),
            new ProgressSnapshotDto(
                p.Phase, p.CurrentItem, p.LastError, p.LastUpdatedUtc, p.SyncRunning, p.EnrichmentRunning,
                p.EnrichmentPaused, p.BackfillDay, p.BackfillDaysCompleted, p.CurrentJobProcessed,
                p.CurrentJobSucceeded, p.CurrentJobFailed, p.RecentLog));
    }

    public static LookupDto Lookup(GalaxyMusicDataset.Data.Entities.TrackLookup lookup) =>
        new(
            lookup.Id,
            lookup.Fingerprint,
            lookup.TrackId,
            lookup.ArtistName,
            lookup.TrackName,
            lookup.AlbumName,
            lookup.Status,
            lookup.CreatedAt,
            lookup.LastAttemptUtc,
            lookup.AttemptCount,
            lookup.MatchedMbid,
            lookup.BestScore,
            lookup.ErrorMessage,
            lookup.QueryUsed);
}
