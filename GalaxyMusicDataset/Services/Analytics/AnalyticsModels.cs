namespace GalaxyMusicDataset.Services.Analytics;

public sealed record OverviewStats(
    int ScrobbleCount,
    int UniqueTracks,
    int UniqueArtists,
    int UniqueAlbums,
    long ListeningTimeMs,
    int PlaysWithDuration,
    int PlaysMissingDuration,
    double PercentMissingDuration,
    int DaysTrackedAllTime,
    int DistinctDaysInRange,
    int CalendarDaysInRange,
    double AveragePerDay,
    double AveragePerActiveDay,
    StreakInfo Streak,
    RecentTrackInfo? MostRecent,
    IReadOnlyList<DailyCount> DailyVolume,
    OverviewStats? AllTime);

public sealed record StreakInfo(int Current, int Longest, DateOnly? CurrentStart, DateOnly? CurrentEnd);

public sealed record RecentTrackInfo(
    long TrackId,
    string Title,
    string ArtistName,
    long ArtistId,
    string? AlbumTitle,
    DateTimeOffset PlayedAt);

public sealed record DailyCount(DateOnly Day, int Count, long DurationMs);

public sealed record RankedItem(
    long Id,
    string Name,
    string? Subtitle,
    int Plays,
    long? DurationMs,
    int Rank,
    int PreviousPlays,
    int Delta,
    double? PercentChange,
    bool IsNew);

public sealed record TopListResult(IReadOnlyList<RankedItem> Items, int Total, bool HasMore);

public sealed record DiscoveryItem(
    long Id,
    string Kind,
    string Name,
    string? Subtitle,
    DateTimeOffset FirstHeard,
    int PlaysInRange);

public sealed record DiscoveryResult(IReadOnlyList<DiscoveryItem> Tracks, IReadOnlyList<DiscoveryItem> Artists);

public sealed record HeatmapCell(int WeekdayMonday0, int HourUtc, int Count, long DurationMs);

public sealed record HeatmapResult(IReadOnlyList<HeatmapCell> Cells, int MaxCount);

public sealed record TimeOfDayBucket(string Name, int StartHour, int EndHourExclusive, int Count, long DurationMs);

public sealed record MonthlyVolume(int Year, int Month, int Count, long DurationMs);

public sealed record TagRollup(string Name, string Source, int Weight, int TrackCount);

public sealed record ArtistDetail(
    long Id,
    string Name,
    string? SortName,
    string? Mbid,
    string? LastFmUrl,
    string? ImageUrl,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<TagRollup> Tags,
    int PlayCount,
    int UniqueTracksPlayed,
    DateTimeOffset? FirstPlayed,
    DateTimeOffset? LastPlayed,
    IReadOnlyList<DailyCount> Timeline,
    IReadOnlyList<RankedItem> TopTracks);

public sealed record SourcePayloadInfo(
    string Source,
    string Status,
    string? ExternalId,
    string? Error,
    string? Json);

public sealed record TrackDetail(
    long Id,
    string Title,
    long ArtistId,
    string ArtistName,
    long? AlbumId,
    string? AlbumTitle,
    string? CoverUrl,
    string? Mbid,
    int? DurationMs,
    string Fingerprint,
    string? LookupStatus,
    double? LookupScore,
    string? Isrc,
    string? MusicVideoUrl,
    string? Summary,
    int PlayCount,
    DateTimeOffset? FirstPlayed,
    DateTimeOffset? LastPlayed,
    IReadOnlyList<DateTimeOffset> PlayedAt,
    IReadOnlyList<TagRollup> Tags,
    IReadOnlyList<SourcePayloadInfo> Sources);

public sealed record DeepCutsResult(
    IReadOnlyList<RankedItem> OneOffs,
    int OneOffTotal,
    IReadOnlyList<RankedItem> Heavy,
    int HeavyTotal,
    int HeavyThreshold,
    bool OneOffsHasMore,
    bool HeavyHasMore);

public sealed record ListeningSession(
    DateTimeOffset Start,
    DateTimeOffset End,
    TimeSpan Length,
    int TrackCount,
    string FirstArtist,
    string LastArtist,
    string? FirstTrack,
    string? LastTrack,
    long? DurationSumMs);

public sealed record SessionsResult(
    IReadOnlyList<ListeningSession> Sessions,
    int SessionCount,
    bool HasMore,
    double AverageLengthMinutes,
    double MedianTracks,
    double RepeatRate,
    int SkipAdjacentCount,
    int ConsecutivePairs);

public sealed record WrappedResult(
    int Year,
    OverviewStats Overview,
    IReadOnlyList<RankedItem> TopArtists,
    IReadOnlyList<RankedItem> TopTracks,
    IReadOnlyList<RankedItem> TopAlbums,
    IReadOnlyList<DiscoveryItem> Discoveries,
    HeatmapResult Heatmap,
    IReadOnlyList<RankedItem> NewArtists,
    RankedItem? MostReplayed,
    int LongestStreak,
    int BusiestHourUtc,
    int BusiestHourCount,
    IReadOnlyList<TagStat> TopGenres);

public sealed record TagStat(
    string Name,
    int Plays,
    int TrackCount,
    long DurationMs,
    IReadOnlyList<string> Sources);

public sealed record TagCloudResult(
    IReadOnlyList<TagStat> Genres,
    IReadOnlyList<TagStat> Tags,
    int TaggedPlayCount,
    int UntaggedPlayCount);

public sealed record TagDetailResult(
    string Name,
    IReadOnlyList<RankedItem> Tracks,
    IReadOnlyList<RankedItem> Artists,
    IReadOnlyList<string> Sources);
