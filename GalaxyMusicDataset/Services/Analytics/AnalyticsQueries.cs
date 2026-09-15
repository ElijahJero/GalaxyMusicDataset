using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Audio;
using GalaxyMusicDataset.Services.Normalization;
using GalaxyMusicDataset.Services.Catalog;
using GalaxyMusicDataset.Services.YouTube;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Analytics;

public sealed class AnalyticsQueries(AppDbContext db, AppTimeZone? timeZone = null)
{
    private readonly AppTimeZone _tz = timeZone ?? AppTimeZone.Utc;

    public const int DefaultTake = 50;
    public const int DefaultHeavyThreshold = 10;
    public const int DefaultSessionGapMinutes = 30;
    public const double SkipAdjacentFraction = 0.3;

    public IQueryable<Scrobble> Filter(TimeRange range, string? search) =>
        AnalyticsQuery.Apply(db.Scrobbles.AsNoTracking(), range, search);

    public async Task<OverviewStats> GetOverview(
        TimeRange range,
        string? search,
        CancellationToken cancellationToken,
        bool includeAllTime = true,
        DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var streak = await GetStreak(now, cancellationToken);
        var daysTracked = await DistinctLocalDays(db.Scrobbles.AsNoTracking(), cancellationToken);
        var stats = await ComputeOverview(Filter(range, search), range, streak, daysTracked, cancellationToken);
        if (!includeAllTime || range.Preset == "all")
        {
            return stats;
        }

        var allRange = TimeRangeParser.Parse("all", null, null, now);
        var allTime = await ComputeOverview(Filter(allRange, search), allRange, streak, daysTracked, cancellationToken);
        return stats with { AllTime = allTime with { DailyVolume = [], AllTime = null } };
    }

    public async Task<StreakInfo> GetStreak(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var stamps = await db.Scrobbles.AsNoTracking()
            .Select(s => s.UnixTimestamp)
            .ToListAsync(cancellationToken);
        var days = stamps
            .Select(_tz.LocalDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        return ComputeStreak(days, _tz.LocalDate(utcNow));
    }

    public async Task<TopListResult> GetTopArtists(
        TimeRange range,
        TimeRange? previousRange,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await Filter(range, search)
            .GroupBy(s => new { s.Track.ArtistId, s.Track.Artist.Name })
            .Select(g => new { Id = g.Key.ArtistId, Name = g.Key.Name, Plays = g.Count(), Duration = g.Sum(s => (long?)s.Track.DurationMs) })
            .ToListAsync(cancellationToken);
        return await RankRows(
            rows.Select(x => new IdNameCount(x.Id, x.Name, null, x.Plays, x.Duration)).ToList(),
            previousRange,
            search,
            take,
            TopKind.Artist,
            cancellationToken);
    }

    public async Task<TopListResult> GetTopTracks(
        TimeRange range,
        TimeRange? previousRange,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await Filter(range, search)
            .GroupBy(s => new { s.TrackId, s.Track.Title, Artist = s.Track.Artist.Name })
            .Select(g => new { Id = g.Key.TrackId, Name = g.Key.Title, Subtitle = g.Key.Artist, Plays = g.Count(), Duration = g.Sum(s => (long?)s.Track.DurationMs) })
            .ToListAsync(cancellationToken);
        return await RankRows(
            rows.Select(x => new IdNameCount(x.Id, x.Name, x.Subtitle, x.Plays, x.Duration)).ToList(),
            previousRange,
            search,
            take,
            TopKind.Track,
            cancellationToken);
    }

    public async Task<TopListResult> GetTopAlbums(
        TimeRange range,
        TimeRange? previousRange,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await Filter(range, search)
            .Where(s => s.Track.AlbumId != null)
            .GroupBy(s => new { AlbumId = s.Track.AlbumId!.Value, Title = s.Track.Album!.Title, Artist = s.Track.Artist.Name })
            .Select(g => new { Id = g.Key.AlbumId, Name = g.Key.Title, Subtitle = g.Key.Artist, Plays = g.Count(), Duration = g.Sum(s => (long?)s.Track.DurationMs) })
            .ToListAsync(cancellationToken);
        return await RankRows(
            rows.Select(x => new IdNameCount(x.Id, x.Name, x.Subtitle, x.Plays, x.Duration)).ToList(),
            previousRange,
            search,
            take,
            TopKind.Album,
            cancellationToken);
    }

    public async Task<DiscoveryResult> GetDiscoveries(
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var fromUnix = range.From.ToUnixTimeSeconds();
        var toUnix = range.To.ToUnixTimeSeconds();
        take = ClampTake(take);

        var trackFirsts = db.Scrobbles.AsNoTracking()
            .GroupBy(s => s.TrackId)
            .Select(g => new { TrackId = g.Key, FirstUnix = g.Min(x => x.UnixTimestamp) })
            .Where(x => x.FirstUnix >= fromUnix && x.FirstUnix < toUnix);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            trackFirsts = trackFirsts.Where(x =>
                db.Tracks.Any(t => t.Id == x.TrackId && (
                    t.Title.ToLower().Contains(term) ||
                    t.Artist.Name.ToLower().Contains(term) ||
                    (t.Album != null && t.Album.Title.ToLower().Contains(term)) ||
                    t.Artist.Aliases.Any(a => a.Name.ToLower().Contains(term)))));
        }

        var trackRows = await trackFirsts
            .OrderByDescending(x => x.FirstUnix)
            .Take(take)
            .Join(db.Tracks.AsNoTracking(), x => x.TrackId, t => t.Id, (x, t) => new
            {
                t.Id,
                t.Title,
                Artist = t.Artist.Name,
                x.FirstUnix
            })
            .ToListAsync(cancellationToken);

        var trackIds = trackRows.Select(t => t.Id).ToList();
        var playsInRange = await Filter(range, search)
            .Where(s => trackIds.Contains(s.TrackId))
            .GroupBy(s => s.TrackId)
            .Select(g => new { TrackId = g.Key, Plays = g.Count() })
            .ToListAsync(cancellationToken);
        var playMap = playsInRange.ToDictionary(x => x.TrackId, x => x.Plays);

        var tracks = trackRows.Select(t => new DiscoveryItem(
            t.Id,
            "track",
            t.Title,
            t.Artist,
            DateTimeOffset.FromUnixTimeSeconds(t.FirstUnix),
            playMap.GetValueOrDefault(t.Id))).ToList();

        var artistFirsts = db.Scrobbles.AsNoTracking()
            .GroupBy(s => s.Track.ArtistId)
            .Select(g => new { ArtistId = g.Key, FirstUnix = g.Min(x => x.UnixTimestamp) })
            .Where(x => x.FirstUnix >= fromUnix && x.FirstUnix < toUnix);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            artistFirsts = artistFirsts.Where(x =>
                db.Artists.Any(a => a.Id == x.ArtistId && (
                    a.Name.ToLower().Contains(term) || a.Aliases.Any(al => al.Name.ToLower().Contains(term)))));
        }

        var artistRows = await artistFirsts
            .OrderByDescending(x => x.FirstUnix)
            .Take(take)
            .Join(db.Artists.AsNoTracking(), x => x.ArtistId, a => a.Id, (x, a) => new { a.Id, a.Name, x.FirstUnix })
            .ToListAsync(cancellationToken);

        var artistIds = artistRows.Select(a => a.Id).ToList();
        var artistPlays = await Filter(range, search)
            .Where(s => artistIds.Contains(s.Track.ArtistId))
            .GroupBy(s => s.Track.ArtistId)
            .Select(g => new { ArtistId = g.Key, Plays = g.Count() })
            .ToListAsync(cancellationToken);
        var artistPlayMap = artistPlays.ToDictionary(x => x.ArtistId, x => x.Plays);

        var artists = artistRows.Select(a => new DiscoveryItem(
            a.Id,
            "artist",
            a.Name,
            null,
            DateTimeOffset.FromUnixTimeSeconds(a.FirstUnix),
            artistPlayMap.GetValueOrDefault(a.Id))).ToList();

        return new DiscoveryResult(tracks, artists);
    }

    public async Task<HeatmapResult> GetHeatmap(TimeRange range, string? search, CancellationToken cancellationToken)
    {
        var plays = await Filter(range, search)
            .Select(s => new { s.UnixTimestamp, Duration = (long?)s.Track.DurationMs ?? 0L })
            .ToListAsync(cancellationToken);
        var rows = plays
            .GroupBy(s => (_tz.WeekdayMonday0(s.UnixTimestamp), _tz.LocalHour(s.UnixTimestamp)))
            .Select(g => new HeatmapCell(g.Key.Item1, g.Key.Item2, g.Count(), g.Sum(s => s.Duration)))
            .ToList();

        return new HeatmapResult(rows, rows.Count == 0 ? 0 : rows.Max(c => c.Count));
    }

    public IReadOnlyList<TimeOfDayBucket> GetTimeOfDayBuckets(HeatmapResult heatmap)
    {
        return new[]
        {
            Bucket("Morning", 5, 11),
            Bucket("Afternoon", 11, 17),
            Bucket("Evening", 17, 22),
            Bucket("Night", 22, 5)
        };

        TimeOfDayBucket Bucket(string name, int start, int endExclusive)
        {
            var cells = heatmap.Cells.Where(c => InBucket(c.HourUtc, start, endExclusive));
            return new TimeOfDayBucket(name, start, endExclusive, cells.Sum(c => c.Count), cells.Sum(c => c.DurationMs));
        }
    }

    public async Task<IReadOnlyList<MonthlyVolume>> GetMonthlyVolume(
        TimeRange range,
        string? search,
        CancellationToken cancellationToken)
    {
        var days = await LoadDaily(Filter(range, search), cancellationToken);

        return days
            .GroupBy(d => (d.Day.Year, d.Day.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthlyVolume(g.Key.Year, g.Key.Month, g.Sum(x => x.Count), g.Sum(x => x.DurationMs)))
            .ToList();
    }

    public async Task<ArtistDetail?> GetArtistDetail(
        long id,
        TimeRange range,
        string? search,
        CancellationToken cancellationToken)
    {
        var artist = await db.Artists.AsNoTracking()
            .Include(a => a.Aliases)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (artist is null)
        {
            return null;
        }

        var plays = Filter(range, search).Where(s => s.Track.ArtistId == id);
        var playCount = await plays.CountAsync(cancellationToken);
        var uniqueTracks = playCount == 0
            ? 0
            : await plays.Select(s => s.TrackId).Distinct().CountAsync(cancellationToken);
        long? first = null;
        long? last = null;
        if (playCount > 0)
        {
            first = await plays.MinAsync(s => s.UnixTimestamp, cancellationToken);
            last = await plays.MaxAsync(s => s.UnixTimestamp, cancellationToken);
        }

        var timeline = await LoadDaily(plays, cancellationToken);

        var topRows = await plays
            .GroupBy(s => new { s.TrackId, s.Track.Title })
            .Select(g => new
            {
                g.Key.TrackId,
                g.Key.Title,
                Plays = g.Count(),
                Duration = g.Sum(s => (long?)s.Track.DurationMs)
            })
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.Title)
            .Take(25)
            .ToListAsync(cancellationToken);
        var topIds = topRows.Select(x => x.TrackId).ToList();
        var albums = await db.Tracks.AsNoTracking()
            .Where(t => topIds.Contains(t.Id))
            .Select(t => new { t.Id, Album = t.Album != null ? t.Album.Title : null })
            .ToListAsync(cancellationToken);
        var albumMap = albums.ToDictionary(x => x.Id, x => x.Album);
        var topTracks = topRows
            .Select((x, i) => new RankedItem(x.TrackId, x.Title, albumMap.GetValueOrDefault(x.TrackId), x.Plays, x.Duration, i + 1, 0, x.Plays, null, false))
            .ToList();

        var tagRows = await db.TrackTags.AsNoTracking()
            .Where(tt => tt.Track.ArtistId == id)
            .Select(tt => new { tt.Tag.Name, tt.Source, tt.Weight, tt.TrackId })
            .ToListAsync(cancellationToken);
        var tags = tagRows
            .GroupBy(t => (t.Name, t.Source))
            .Select(g => new TagRollup(g.Key.Name, g.Key.Source.ToString(), g.Sum(x => x.Weight), g.Select(x => x.TrackId).Distinct().Count()))
            .OrderByDescending(t => t.Weight)
            .Take(40)
            .ToList();

        return new ArtistDetail(
            artist.Id,
            artist.Name,
            artist.SortName,
            artist.Mbid,
            artist.LastFmUrl,
            artist.ImageUrl,
            artist.Aliases.Select(a => a.Name).Distinct().OrderBy(n => n).ToList(),
            tags,
            playCount,
            uniqueTracks,
            first is null ? null : DateTimeOffset.FromUnixTimeSeconds(first.Value),
            last is null ? null : DateTimeOffset.FromUnixTimeSeconds(last.Value),
            timeline,
            topTracks);
    }

    public async Task<PagedResult<RecentPlay>> GetRecentPlays(
        TimeRange range,
        string? search,
        int take,
        int page,
        CancellationToken cancellationToken)
    {
        take = ClampTake(take);
        page = Math.Max(1, page);
        var query = Filter(range, search).OrderByDescending(s => s.UnixTimestamp);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * take)
            .Take(take)
            .Select(s => new RecentPlay(
                s.Id,
                s.TrackId,
                s.Track.Title,
                s.Track.ArtistId,
                s.Track.Artist.Name,
                s.Track.AlbumId,
                s.Track.Album == null ? null : s.Track.Album.Title,
                s.Track.Album == null ? null : s.Track.Album.CoverUrl,
                s.PlayedAt))
            .ToListAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)take));
        return new PagedResult<RecentPlay>(rows, total, page, take, totalPages, page < totalPages);
    }

    public async Task<TrackDetail?> GetTrackDetail(long id, CancellationToken cancellationToken)
    {
        var track = await db.Tracks.AsNoTracking()
            .Include(t => t.Artist).ThenInclude(a => a.Aliases)
            .Include(t => t.Album)
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.SourcePayloads)
            .Include(t => t.AudioProfile)
                .ThenInclude(p => p!.Labels)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (track is null)
        {
            return null;
        }

        var lookup = await db.TrackLookups.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Fingerprint == track.Fingerprint, cancellationToken);
        var stamps = await db.Scrobbles.AsNoTracking()
            .Where(s => s.TrackId == id)
            .OrderBy(s => s.UnixTimestamp)
            .Select(s => s.UnixTimestamp)
            .ToListAsync(cancellationToken);
        var playedAt = stamps.Select(DateTimeOffset.FromUnixTimeSeconds).ToList();

        var tags = track.Tags
            .OrderByDescending(t => t.Weight)
            .Select(t => new TagRollup(t.Tag.Name, t.Source.ToString(), t.Weight, 1))
            .ToList();
        var sources = track.SourcePayloads
            .OrderBy(p => p.Source)
            .Select(p => new SourcePayloadInfo(p.Source.ToString(), p.Status.ToString(), p.ExternalId, p.ErrorMessage, p.PayloadJson))
            .ToList();

        return new TrackDetail(
            track.Id,
            track.Title,
            track.ArtistId,
            track.Artist.Name,
            track.AlbumId,
            track.Album?.Title,
            track.Album?.CoverUrl,
            track.Mbid,
            track.VocaDbSongId,
            track.UtaiteDbSongId,
            track.TouhouDbSongId,
            track.DurationMs,
            track.Fingerprint,
            lookup?.Status.ToString(),
            lookup?.BestScore,
            track.Isrc,
            track.MusicVideoUrl,
            YouTubeMusicLinks.OpenUrl(track.Artist.Name, track.Title, track.MusicVideoUrl),
            track.Summary,
            stamps.Count,
            playedAt.FirstOrDefault() is var first && stamps.Count > 0 ? first : null,
            playedAt.LastOrDefault() is var last && stamps.Count > 0 ? last : null,
            playedAt,
            tags,
            sources,
            track.DiscogsReleaseId,
            track.TheAudioDbTrackId,
            track.Album?.Mbid,
            CatalogLinks.ForTrack(
                track.Mbid,
                track.Album?.Mbid,
                track.DiscogsReleaseId,
                track.VocaDbSongId,
                track.UtaiteDbSongId,
                track.TouhouDbSongId,
                track.TheAudioDbTrackId,
                track.SourcePayloads.FirstOrDefault(p => p.Source == EnrichmentSource.LastFm)?.PayloadJson,
                track.Artist.Name,
                track.Title),
            track.AudioProfile is null ? null : AudioProfileService.ToView(track.AudioProfile));
    }

    public async Task<DeepCutsResult> GetDeepCuts(
        TimeRange range,
        string? search,
        int heavyThreshold,
        int take,
        CancellationToken cancellationToken)
    {
        if (heavyThreshold < 2)
        {
            heavyThreshold = DefaultHeavyThreshold;
        }

        take = ClampTake(take);
        var rows = await Filter(range, search)
            .GroupBy(s => new { s.TrackId, s.Track.Title, Artist = s.Track.Artist.Name })
            .Select(g => new IdNameCount(
                g.Key.TrackId,
                g.Key.Title,
                g.Key.Artist,
                g.Count(),
                g.Sum(s => (long?)s.Track.DurationMs)))
            .ToListAsync(cancellationToken);

        var oneOffs = rows.Where(g => g.Plays == 1).OrderBy(g => g.Name).ThenBy(g => g.Subtitle).ToList();
        var heavy = rows.Where(g => g.Plays >= heavyThreshold).OrderByDescending(g => g.Plays).ThenBy(g => g.Name).ToList();

        return new DeepCutsResult(
            ToRanked(oneOffs.Take(take).ToList()),
            oneOffs.Count,
            ToRanked(heavy.Take(take).ToList()),
            heavy.Count,
            heavyThreshold,
            oneOffs.Count > take,
            heavy.Count > take);
    }

    public async Task<SessionsResult> GetSessions(
        TimeRange range,
        string? search,
        int gapMinutes,
        int take,
        CancellationToken cancellationToken)
    {
        if (gapMinutes <= 0)
        {
            gapMinutes = DefaultSessionGapMinutes;
        }

        take = ClampTake(take);
        var plays = await Filter(range, search)
            .OrderBy(s => s.UnixTimestamp)
            .Select(s => new PlayRow(s.UnixTimestamp, s.TrackId, s.Track.DurationMs, s.Track.Artist.Name, s.Track.Title))
            .ToListAsync(cancellationToken);

        var gapSeconds = gapMinutes * 60L;
        var sessions = new List<ListeningSession>();
        var start = 0;
        for (var i = 1; i <= plays.Count; i++)
        {
            var boundary = i == plays.Count || plays[i].UnixTimestamp - plays[i - 1].UnixTimestamp > gapSeconds;
            if (!boundary)
            {
                continue;
            }

            var slice = plays.GetRange(start, i - start);
            var firstPlay = slice[0];
            var lastPlay = slice[^1];
            var startAt = DateTimeOffset.FromUnixTimeSeconds(firstPlay.UnixTimestamp);
            var endAt = DateTimeOffset.FromUnixTimeSeconds(lastPlay.UnixTimestamp);
            var durationSum = slice.Where(p => p.DurationMs is not null).Sum(p => (long)p.DurationMs!);
            var length = endAt - startAt;
            if (length == TimeSpan.Zero && durationSum > 0)
            {
                length = TimeSpan.FromMilliseconds(durationSum);
            }

            sessions.Add(new ListeningSession(
                startAt,
                endAt,
                length,
                slice.Count,
                firstPlay.Artist,
                lastPlay.Artist,
                firstPlay.Title,
                lastPlay.Title,
                durationSum == 0 ? null : durationSum));
            start = i;
        }

        var pairs = Math.Max(0, plays.Count - 1);
        var repeats = 0;
        var skips = 0;
        for (var i = 0; i < pairs; i++)
        {
            if (plays[i].TrackId == plays[i + 1].TrackId)
            {
                repeats++;
            }

            if (plays[i].DurationMs is int duration and > 0)
            {
                var gap = plays[i + 1].UnixTimestamp - plays[i].UnixTimestamp;
                if (gap >= 0 && gap < duration / 1000.0 * SkipAdjacentFraction)
                {
                    skips++;
                }
            }
        }

        var trackCounts = sessions.Select(s => s.TrackCount).OrderBy(n => n).ToList();
        double median = 0;
        if (trackCounts.Count > 0)
        {
            var mid = trackCounts.Count / 2;
            median = trackCounts.Count % 2 == 1
                ? trackCounts[mid]
                : (trackCounts[mid - 1] + trackCounts[mid]) / 2.0;
        }

        var avgMinutes = sessions.Count == 0 ? 0 : sessions.Average(s => s.Length.TotalMinutes);
        var newestFirst = sessions.AsEnumerable().Reverse().Take(take).ToList();
        return new SessionsResult(
            newestFirst,
            sessions.Count,
            sessions.Count > take,
            avgMinutes,
            median,
            pairs == 0 ? 0 : repeats / (double)pairs,
            skips,
            pairs);
    }

    public async Task<WrappedResult> GetWrapped(int year, string? search, CancellationToken cancellationToken)
    {
        var range = TimeRangeParser.ForCalendarYear(year, _tz.Zone);
        var overview = await GetOverview(range, search, cancellationToken, includeAllTime: false);
        var previous = TimeRangeParser.PreviousWindow(range);
        var artists = await GetTopArtists(range, previous, search, 10, cancellationToken);
        var tracks = await GetTopTracks(range, previous, search, 10, cancellationToken);
        var albums = await GetTopAlbums(range, previous, search, 10, cancellationToken);
        var discoveries = await GetDiscoveries(range, search, 20, cancellationToken);
        var heatmap = await GetHeatmap(range, search, cancellationToken);
        var tags = await GetTagCloud(range, search, 10, cancellationToken);
        var newArtists = discoveries.Artists
            .Select((d, i) => new RankedItem(d.Id, d.Name, null, d.PlaysInRange, null, i + 1, 0, d.PlaysInRange, null, true))
            .ToList();
        var busiest = heatmap.Cells
            .GroupBy(c => c.HourUtc)
            .Select(g => new { Hour = g.Key, Count = g.Sum(c => c.Count) })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        var longest = LongestRun(overview.DailyVolume.Select(d => d.Day));

        return new WrappedResult(
            year,
            overview,
            artists.Items,
            tracks.Items,
            albums.Items,
            discoveries.Tracks,
            heatmap,
            newArtists,
            tracks.Items.FirstOrDefault(),
            longest,
            busiest?.Hour ?? 0,
            busiest?.Count ?? 0,
            tags.Genres);
    }

    public async Task<WrappedHtmlExport> GetWrappedHtmlExport(int year, string? search, CancellationToken cancellationToken)
    {
        var wrapped = await GetWrapped(year, search, cancellationToken);
        const int take = 5;
        var topArtists = wrapped.TopArtists.Take(take).ToList();
        var topTracks = wrapped.TopTracks.Take(take).ToList();
        var topAlbums = wrapped.TopAlbums.Take(take).ToList();
        var newArtists = wrapped.NewArtists.Take(take).ToList();

        var artistIds = topArtists.Select(x => x.Id).Concat(newArtists.Select(x => x.Id)).Distinct().ToList();
        var trackIds = topTracks.Select(x => x.Id).ToList();
        if (wrapped.MostReplayed is not null)
        {
            trackIds.Add(wrapped.MostReplayed.Id);
        }

        trackIds = trackIds.Distinct().ToList();
        var albumIds = topAlbums.Select(x => x.Id).ToList();

        var artistImages = artistIds.Count == 0
            ? new Dictionary<long, string?>()
            : await db.Artists.AsNoTracking()
                .Where(a => artistIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.ImageUrl, cancellationToken);

        var trackCovers = trackIds.Count == 0
            ? new Dictionary<long, string?>()
            : await db.Tracks.AsNoTracking()
                .Where(t => trackIds.Contains(t.Id))
                .Select(t => new { t.Id, Cover = t.Album != null ? t.Album.CoverUrl : null })
                .ToDictionaryAsync(x => x.Id, x => x.Cover, cancellationToken);

        var albumCovers = albumIds.Count == 0
            ? new Dictionary<long, string?>()
            : await db.Albums.AsNoTracking()
                .Where(a => albumIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.CoverUrl, cancellationToken);

        string? listener = null;
        if (await db.SyncStates.AsNoTracking().AnyAsync(cancellationToken))
        {
            listener = await db.SyncStates.AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => s.LastFmUsername)
                .FirstOrDefaultAsync(cancellationToken);
        }

        static WrappedMediaItem ToMedia(RankedItem item, string? image) =>
            new(item.Id, item.Name, item.Subtitle, item.Plays, item.DurationMs, item.Rank, BlankToNull(image));

        WrappedMediaItem? mostReplayed = null;
        if (wrapped.MostReplayed is not null)
        {
            trackCovers.TryGetValue(wrapped.MostReplayed.Id, out var cover);
            mostReplayed = ToMedia(wrapped.MostReplayed, cover);
        }

        return new WrappedHtmlExport(
            wrapped.Year,
            BlankToNull(listener),
            wrapped.Overview,
            topArtists.Select(a => ToMedia(a, artistImages.GetValueOrDefault(a.Id))).ToList(),
            topTracks.Select(t => ToMedia(t, trackCovers.GetValueOrDefault(t.Id))).ToList(),
            topAlbums.Select(a => ToMedia(a, albumCovers.GetValueOrDefault(a.Id))).ToList(),
            wrapped.TopGenres.Take(take).ToList(),
            mostReplayed,
            wrapped.LongestStreak,
            wrapped.BusiestHourUtc,
            wrapped.BusiestHourCount,
            newArtists.Select(a => ToMedia(a, artistImages.GetValueOrDefault(a.Id))).ToList(),
            wrapped.Discoveries.Take(take).ToList());
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<IReadOnlyList<int>> GetYears(CancellationToken cancellationToken)
    {
        if (!await db.Scrobbles.AsNoTracking().AnyAsync(cancellationToken))
        {
            return [];
        }

        var min = await db.Scrobbles.AsNoTracking().MinAsync(s => s.UnixTimestamp, cancellationToken);
        var max = await db.Scrobbles.AsNoTracking().MaxAsync(s => s.UnixTimestamp, cancellationToken);
        var y0 = _tz.LocalYear(min);
        var y1 = _tz.LocalYear(max);
        return Enumerable.Range(y0, y1 - y0 + 1).Reverse().ToList();
    }

    public async Task<TagCloudResult> GetTagCloud(
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        take = ClampTake(take);
        var playRows = await Filter(range, search)
            .GroupBy(s => s.TrackId)
            .Select(g => new TrackPlayRow(g.Key, g.Count(), g.Sum(s => (long?)s.Track.DurationMs) ?? 0L))
            .ToListAsync(cancellationToken);

        var playMap = playRows.ToDictionary(x => x.TrackId);
        if (playMap.Count == 0)
        {
            return new TagCloudResult([], [], 0, 0);
        }

        var trackIds = playMap.Keys.ToList();
        var links = await db.TrackTags.AsNoTracking()
            .Where(tt => trackIds.Contains(tt.TrackId))
            .Select(tt => new TagLinkRow(tt.TrackId, tt.Tag.Name, tt.Tag.NormalizedName, tt.Source, tt.Weight))
            .ToListAsync(cancellationToken);

        var taggedTrackIds = links.Select(l => l.TrackId).ToHashSet();
        var taggedPlays = taggedTrackIds.Sum(id => playMap[id].Plays);
        var untaggedPlays = playRows.Where(x => !taggedTrackIds.Contains(x.TrackId)).Sum(x => x.Plays);

        var genres = RollupTags(links.Where(l => IsGenreLike(l.Source, l.Weight)), playMap, take);
        var allTags = RollupTags(links, playMap, take);
        return new TagCloudResult(genres, allTags, taggedPlays, untaggedPlays);
    }

    public async Task<TagDetailResult?> GetTagDetail(
        string name,
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        take = ClampTake(take);
        var normalized = TextNormalizer.Normalize(name);
        var tag = await db.Tags.AsNoTracking()
            .FirstOrDefaultAsync(t => t.NormalizedName == normalized || t.Name == name, cancellationToken);
        if (tag is null)
        {
            return null;
        }

        var trackIds = await db.TrackTags.AsNoTracking()
            .Where(tt => tt.TagId == tag.Id)
            .Select(tt => tt.TrackId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (trackIds.Count == 0)
        {
            return new TagDetailResult(tag.Name, [], [], []);
        }

        var rows = await Filter(range, search)
            .Where(s => trackIds.Contains(s.TrackId))
            .GroupBy(s => new { s.TrackId, s.Track.Title, Artist = s.Track.Artist.Name, s.Track.ArtistId })
            .Select(g => new
            {
                g.Key.TrackId,
                g.Key.Title,
                g.Key.Artist,
                g.Key.ArtistId,
                Plays = g.Count(),
                Duration = g.Sum(s => (long?)s.Track.DurationMs)
            })
            .ToListAsync(cancellationToken);

        var tracks = rows
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.Title)
            .Take(take)
            .Select((x, i) => new RankedItem(x.TrackId, x.Title, x.Artist, x.Plays, x.Duration, i + 1, 0, x.Plays, null, false))
            .ToList();
        var artists = rows
            .GroupBy(x => new { x.ArtistId, x.Artist })
            .Select(g => new { g.Key.ArtistId, g.Key.Artist, Plays = g.Sum(x => x.Plays), Duration = g.Sum(x => x.Duration ?? 0) })
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.Artist)
            .Take(take)
            .Select((x, i) => new RankedItem(x.ArtistId, x.Artist, null, x.Plays, x.Duration, i + 1, 0, x.Plays, null, false))
            .ToList();
        var sources = await db.TrackTags.AsNoTracking()
            .Where(tt => tt.TagId == tag.Id)
            .Select(tt => tt.Source.ToString())
            .Distinct()
            .ToListAsync(cancellationToken);

        return new TagDetailResult(tag.Name, tracks, artists, sources.OrderBy(s => s).ToList());
    }

    public static bool IsGenreLike(EnrichmentSource source, int weight) => source switch
    {
        EnrichmentSource.Discogs => true,
        EnrichmentSource.TheAudioDb => weight >= 40,
        EnrichmentSource.MusicBrainz => weight >= 80,
        EnrichmentSource.VocaDb or EnrichmentSource.UtaiteDb or EnrichmentSource.TouhouDb => weight >= 80,
        _ => false
    };

    private static List<TagStat> RollupTags(
        IEnumerable<TagLinkRow> links,
        Dictionary<long, TrackPlayRow> playMap,
        int take)
    {
        return links
            .GroupBy(l => l.NormalizedName)
            .Select(g =>
            {
                var trackIds = g.Select(x => x.TrackId).Distinct().ToList();
                var plays = trackIds.Sum(id => playMap.GetValueOrDefault(id)?.Plays ?? 0);
                var duration = trackIds.Sum(id => playMap.GetValueOrDefault(id)?.Duration ?? 0);
                var display = g.OrderByDescending(x => x.Weight).First().Name;
                var sources = g.Select(x => x.Source.ToString()).Distinct().OrderBy(s => s).ToList();
                return new TagStat(display, plays, trackIds.Count, duration, sources);
            })
            .OrderByDescending(t => t.Plays)
            .ThenBy(t => t.Name)
            .Take(take)
            .ToList();
    }

    private async Task<OverviewStats> ComputeOverview(
        IQueryable<Scrobble> query,
        TimeRange range,
        StreakInfo streak,
        int daysTrackedAllTime,
        CancellationToken cancellationToken)
    {
        var scrobbleCount = await query.CountAsync(cancellationToken);
        var uniqueTracks = await query.Select(s => s.TrackId).Distinct().CountAsync(cancellationToken);
        var uniqueArtists = await query.Select(s => s.Track.ArtistId).Distinct().CountAsync(cancellationToken);
        var uniqueAlbums = await query
            .Where(s => s.Track.AlbumId != null)
            .Select(s => s.Track.AlbumId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);
        var playsWithDuration = await query.CountAsync(s => s.Track.DurationMs != null, cancellationToken);
        var listeningTime = playsWithDuration == 0
            ? 0
            : await query.Where(s => s.Track.DurationMs != null).SumAsync(s => (long)s.Track.DurationMs!, cancellationToken);
        var missing = scrobbleCount - playsWithDuration;
        var missingPct = scrobbleCount == 0 ? 0 : missing * 100.0 / scrobbleCount;

        var daily = await LoadDaily(query, cancellationToken);

        var calendarDays = range.Preset == "all" ? Math.Max(1, daysTrackedAllTime) : TimeRangeParser.CalendarDays(range);
        var distinctDays = daily.Count;
        var avgPerDay = scrobbleCount / (double)calendarDays;
        var avgActive = distinctDays == 0 ? 0 : scrobbleCount / (double)distinctDays;

        RecentTrackInfo? recent = null;
        var latest = await query
            .OrderByDescending(s => s.UnixTimestamp)
            .Select(s => new
            {
                s.TrackId,
                s.Track.Title,
                Artist = s.Track.Artist.Name,
                ArtistId = s.Track.ArtistId,
                Album = s.Track.Album != null ? s.Track.Album.Title : null,
                s.UnixTimestamp
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null)
        {
            recent = new RecentTrackInfo(
                latest.TrackId,
                latest.Title,
                latest.Artist,
                latest.ArtistId,
                latest.Album,
                DateTimeOffset.FromUnixTimeSeconds(latest.UnixTimestamp));
        }

        return new OverviewStats(
            scrobbleCount,
            uniqueTracks,
            uniqueArtists,
            uniqueAlbums,
            listeningTime,
            playsWithDuration,
            missing,
            missingPct,
            daysTrackedAllTime,
            distinctDays,
            calendarDays,
            avgPerDay,
            avgActive,
            streak,
            recent,
            daily,
            null);
    }

    private async Task<TopListResult> RankRows(
        List<IdNameCount> current,
        TimeRange? previousRange,
        string? search,
        int take,
        TopKind kind,
        CancellationToken cancellationToken)
    {
        take = ClampTake(take);
        var total = current.Count;
        var items = current
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.Name)
            .Take(take)
            .ToList();

        Dictionary<long, int> previousPlays = [];
        if (previousRange is not null && items.Count > 0)
        {
            var ids = items.Select(x => x.Id).ToList();
            var prevQuery = Filter(previousRange, search);
            var prev = kind switch
            {
                TopKind.Artist => await prevQuery.Where(s => ids.Contains(s.Track.ArtistId))
                    .GroupBy(s => s.Track.ArtistId)
                    .Select(g => new { Id = g.Key, Plays = g.Count() })
                    .ToListAsync(cancellationToken),
                TopKind.Album => await prevQuery.Where(s => s.Track.AlbumId != null && ids.Contains(s.Track.AlbumId.Value))
                    .GroupBy(s => s.Track.AlbumId!.Value)
                    .Select(g => new { Id = g.Key, Plays = g.Count() })
                    .ToListAsync(cancellationToken),
                _ => await prevQuery.Where(s => ids.Contains(s.TrackId))
                    .GroupBy(s => s.TrackId)
                    .Select(g => new { Id = g.Key, Plays = g.Count() })
                    .ToListAsync(cancellationToken)
            };
            previousPlays = prev.ToDictionary(x => x.Id, x => x.Plays);
        }

        var ranked = items.Select((x, i) =>
        {
            var prevPlays = previousPlays.GetValueOrDefault(x.Id);
            var isNew = previousRange is not null && !previousPlays.ContainsKey(x.Id);
            var delta = x.Plays - prevPlays;
            double? pct = prevPlays > 0 ? delta / (double)prevPlays * 100.0 : null;
            return new RankedItem(x.Id, x.Name, x.Subtitle, x.Plays, x.DurationMs, i + 1, prevPlays, delta, pct, isNew);
        }).ToList();

        return new TopListResult(ranked, total, total > take);
    }

    private static List<RankedItem> ToRanked(IReadOnlyList<IdNameCount> rows) =>
        rows.Select((x, i) => new RankedItem(x.Id, x.Name, x.Subtitle, x.Plays, x.DurationMs, i + 1, 0, x.Plays, null, false)).ToList();

    private async Task<List<DailyCount>> LoadDaily(IQueryable<Scrobble> query, CancellationToken cancellationToken)
    {
        var rows = await query
            .Select(s => new { s.UnixTimestamp, Duration = (long?)s.Track.DurationMs ?? 0L })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(s => _tz.LocalDate(s.UnixTimestamp))
            .OrderBy(g => g.Key)
            .Select(g => new DailyCount(g.Key, g.Count(), g.Sum(s => s.Duration)))
            .ToList();
    }

    private async Task<int> DistinctLocalDays(IQueryable<Scrobble> scrobbles, CancellationToken cancellationToken)
    {
        var stamps = await scrobbles.Select(s => s.UnixTimestamp).ToListAsync(cancellationToken);
        return stamps.Select(_tz.LocalDate).Distinct().Count();
    }

    public static StreakInfo ComputeStreak(IReadOnlyList<DateOnly> sortedDays, DateOnly today)
    {
        var longest = LongestRun(sortedDays);
        if (sortedDays.Count == 0)
        {
            return new StreakInfo(0, 0, null, null);
        }

        var set = sortedDays.ToHashSet();
        var cursor = today;
        if (!set.Contains(cursor))
        {
            cursor = today.AddDays(-1);
            if (!set.Contains(cursor))
            {
                return new StreakInfo(0, longest, null, null);
            }
        }

        var end = cursor;
        var start = cursor;
        while (set.Contains(start.AddDays(-1)))
        {
            start = start.AddDays(-1);
        }

        return new StreakInfo(end.DayNumber - start.DayNumber + 1, longest, start, end);
    }

    public static int LongestRun(IEnumerable<DateOnly> days)
    {
        var sorted = days.Distinct().OrderBy(d => d).ToList();
        var best = 0;
        var run = 0;
        DateOnly? prev = null;
        foreach (var day in sorted)
        {
            run = prev is not null && day == prev.Value.AddDays(1) ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = day;
        }

        return best;
    }

    private static bool InBucket(int hour, int start, int endExclusive)
    {
        if (start < endExclusive)
        {
            return hour >= start && hour < endExclusive;
        }

        return hour >= start || hour < endExclusive;
    }

    private static int ClampTake(int take) => take is < 1 or > 500 ? DefaultTake : take;

    public async Task<AudioAnalyticsResult> GetAudioAnalytics(
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        take = ClampTake(take);
        var playRows = await Filter(range, search)
            .GroupBy(s => s.TrackId)
            .Select(g => new TrackPlayRow(g.Key, g.Count(), g.Sum(s => (long?)s.Track.DurationMs) ?? 0L))
            .ToListAsync(cancellationToken);
        var playMap = playRows.ToDictionary(x => x.TrackId);
        if (playMap.Count == 0)
        {
            return EmptyAudio();
        }

        var trackIds = playMap.Keys.ToList();
        var profiles = await db.TrackAudioProfiles.AsNoTracking()
            .Include(p => p.Labels)
            .Where(p => trackIds.Contains(p.TrackId))
            .ToListAsync(cancellationToken);
        var profiledIds = profiles.Select(p => p.TrackId).ToHashSet();
        var profiledPlays = profiledIds.Sum(id => playMap[id].Plays);
        var unprofiledPlays = playRows.Where(x => !profiledIds.Contains(x.TrackId)).Sum(x => x.Plays);
        var percent = playRows.Sum(x => x.Plays) == 0
            ? 0
            : Math.Round(100.0 * profiledPlays / playRows.Sum(x => x.Plays), 1);

        double? Weighted(Func<TrackAudioProfile, double?> selector)
        {
            double sum = 0;
            var weight = 0;
            foreach (var profile in profiles)
            {
                if (selector(profile) is not double value)
                {
                    continue;
                }

                var plays = playMap[profile.TrackId].Plays;
                sum += value * plays;
                weight += plays;
            }

            return weight == 0 ? null : sum / weight;
        }

        var moodAverages = new List<NamedAverage>();
        AddMood(moodAverages, "party", Weighted(p => p.MoodParty));
        AddMood(moodAverages, "happy", Weighted(p => p.MoodHappy));
        AddMood(moodAverages, "aggressive", Weighted(p => p.MoodAggressive));
        AddMood(moodAverages, "relaxed", Weighted(p => p.MoodRelaxed));
        AddMood(moodAverages, "sad", Weighted(p => p.MoodSad));
        var topMood = moodAverages.OrderByDescending(m => m.Value).Select(m => m.Name).FirstOrDefault();

        var bpmBuckets = new Dictionary<string, (int Tracks, int Plays)>(StringComparer.Ordinal);
        var keys = new Dictionary<string, (int Tracks, int Plays)>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var plays = playMap[profile.TrackId].Plays;
            if (profile.Bpm is double bpm)
            {
                var bucket = AudioBpm.Bucket(bpm).Name;
                bpmBuckets.TryGetValue(bucket, out var current);
                bpmBuckets[bucket] = (current.Tracks + 1, current.Plays + plays);
            }

            var keyDisplay = AudioProfileMapper.KeyDisplay(profile.Key, profile.Scale);
            if (!string.IsNullOrWhiteSpace(keyDisplay))
            {
                keys.TryGetValue(keyDisplay, out var current);
                keys[keyDisplay] = (current.Tracks + 1, current.Plays + plays);
            }
        }

        var genreFolders = RollupGenreFolders(profiles, playMap);
        var genres = genreFolders
            .Select(f => new TagStat(f.Name, f.Plays, f.TrackCount, f.DurationMs, ["Essentia"]))
            .Take(take)
            .ToList();

        return new AudioAnalyticsResult(
            profiledPlays,
            unprofiledPlays,
            profiles.Count,
            playRows.Count - profiles.Count,
            percent,
            Weighted(p => p.Bpm),
            Weighted(p => p.Danceability),
            Weighted(p => p.Voice),
            Weighted(p => p.Acoustic),
            Weighted(p => p.Electronic),
            Weighted(p => p.Approachability),
            Weighted(p => p.Engagement),
            topMood,
            moodAverages,
            NamedCounts(bpmBuckets, AudioBpm.Buckets.Select(b => b.Name).ToArray()),
            keys.OrderByDescending(kv => kv.Value.Plays)
                .ThenBy(kv => kv.Key)
                .Take(take)
                .Select(kv => new NamedCount(kv.Key, kv.Value.Tracks, kv.Value.Plays))
                .ToList(),
            genres,
            genreFolders,
            RollupAudioLabels(profiles, AudioLabelKind.Theme, playMap, take),
            RollupAudioLabels(profiles, AudioLabelKind.Instrument, playMap, take));
    }

    public Task<AudioLabelDetailResult?> GetAudioLabelDetail(
        AudioLabelKind kind,
        string name,
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken) =>
        GetAudioLabelDetail(kind.ToString(), name, range, search, take, cancellationToken);

    public async Task<AudioLabelDetailResult?> GetAudioLabelDetail(
        string? kind,
        string name,
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        take = ClampTake(take);
        var kindKey = kind.Trim().ToLowerInvariant();
        if (kindKey is "bpm" or "tempo")
        {
            return await GetAudioBpmDetail(name, range, search, take, cancellationToken);
        }

        if (kindKey == "key")
        {
            return await GetAudioKeyDetail(name, range, search, take, cancellationToken);
        }

        if (!Enum.TryParse<AudioLabelKind>(kind, true, out var parsedKind))
        {
            return null;
        }

        var needle = name.Trim();
        var folder = AudioGenrePath.IsFolderQuery(parsedKind, needle);
        var labels = await db.TrackAudioLabels.AsNoTracking()
            .Where(l => l.Kind == parsedKind)
            .Select(l => new { l.TrackId, l.Name })
            .ToListAsync(cancellationToken);
        var matches = labels.Where(l => AudioGenrePath.Matches(l.Name, parsedKind, needle)).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        var sample = matches.OrderBy(m => m.Name).First();
        var display = AudioGenrePath.DisplayName(sample.Name, needle);
        var sampleSplit = AudioGenrePath.Split(sample.Name);
        var path = folder ? display : AudioGenrePath.Join(sampleSplit.Primary, sampleSplit.Sub);
        string? parent = folder || parsedKind != AudioLabelKind.Genre ? null : sampleSplit.Primary;
        var trackIds = matches.Select(m => m.TrackId).Distinct().ToList();
        var ranked = await RankAudioMatches(trackIds, range, search, take, cancellationToken);

        IReadOnlyList<AudioGenreFolder> children = [];
        if (folder)
        {
            children = matches
                .Where(m => ranked.PlayMap.ContainsKey(m.TrackId))
                .Select(m =>
                {
                    var split = AudioGenrePath.Split(m.Name);
                    return (m.TrackId, split.Primary, split.Sub);
                })
                .Where(x => x.Sub is not null)
                .GroupBy(x => x.Sub!, StringComparer.OrdinalIgnoreCase)
                .Select(g => FolderFromGroup(g.Key, g.First().Primary, g.Select(x => x.TrackId), ranked.PlayMap))
                .OrderByDescending(c => c.Plays)
                .ThenBy(c => c.Name)
                .ToList();
        }

        return new AudioLabelDetailResult(
            parsedKind.ToString().ToLowerInvariant(),
            display,
            path,
            parent,
            folder,
            children,
            ranked.Tracks,
            ranked.Artists);
    }

    private async Task<AudioLabelDetailResult?> GetAudioBpmDetail(
        string name,
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        if (!AudioBpm.TryResolve(name, out var bucket))
        {
            return null;
        }

        var min = bucket.MinInclusive;
        var maxExclusive = bucket.MaxExclusive;
        IQueryable<TrackAudioProfile> profiles = db.TrackAudioProfiles.AsNoTracking();
        if (min is not null && maxExclusive is not null)
        {
            profiles = profiles.Where(p => p.Bpm != null && p.Bpm >= min && p.Bpm < maxExclusive);
        }
        else if (min is not null)
        {
            profiles = profiles.Where(p => p.Bpm != null && p.Bpm >= min);
        }
        else if (maxExclusive is not null)
        {
            profiles = profiles.Where(p => p.Bpm != null && p.Bpm < maxExclusive);
        }
        else
        {
            profiles = profiles.Where(p => p.Bpm != null);
        }

        var ids = await profiles.Select(p => p.TrackId).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        var ranked = await RankAudioMatches(ids, range, search, take, cancellationToken);
        if (ranked.Tracks.Count == 0)
        {
            return null;
        }

        return new AudioLabelDetailResult("bpm", bucket.Name, bucket.Slug, null, false, [], ranked.Tracks, ranked.Artists);
    }

    private async Task<AudioLabelDetailResult?> GetAudioKeyDetail(
        string name,
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var needle = name.Trim();
        var (key, scale) = AudioProfileMapper.SplitKey(needle, null);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var keyLower = key.ToLower();
        var scaleLower = scale?.ToLower();
        var candidates = await db.TrackAudioProfiles.AsNoTracking()
            .Where(p => p.Key != null && p.Key != "" && p.Key.ToLower() == keyLower)
            .Select(p => new { p.TrackId, p.Key, p.Scale })
            .ToListAsync(cancellationToken);
        var ids = candidates
            .Where(p => AudioKeys.Matches(p.Key, p.Scale, needle))
            .Select(p => p.TrackId)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return null;
        }

        var ranked = await RankAudioMatches(ids, range, search, take, cancellationToken);
        if (ranked.Tracks.Count == 0)
        {
            return null;
        }

        var display = AudioProfileMapper.KeyDisplay(key, scaleLower) ?? needle;
        return new AudioLabelDetailResult("key", display, display, null, false, [], ranked.Tracks, ranked.Artists);
    }

    private async Task<AudioMatchRanking> RankAudioMatches(
        IReadOnlyList<long> trackIds,
        TimeRange range,
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await Filter(range, search)
            .Where(s => trackIds.Contains(s.TrackId))
            .GroupBy(s => new { s.TrackId, s.Track.Title, Artist = s.Track.Artist.Name, s.Track.ArtistId })
            .Select(g => new
            {
                g.Key.TrackId,
                g.Key.Title,
                g.Key.Artist,
                g.Key.ArtistId,
                Plays = g.Count(),
                Duration = g.Sum(s => (long?)s.Track.DurationMs)
            })
            .ToListAsync(cancellationToken);

        var playMap = rows.ToDictionary(
            x => x.TrackId,
            x => new TrackPlayRow(x.TrackId, x.Plays, x.Duration ?? 0L));
        var tracks = rows
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.Title)
            .Take(take)
            .Select((x, i) => new RankedItem(x.TrackId, x.Title, x.Artist, x.Plays, x.Duration, i + 1, 0, x.Plays, null, false))
            .ToList();
        var artists = rows
            .GroupBy(x => new { x.ArtistId, x.Artist })
            .Select(g => new { g.Key.ArtistId, g.Key.Artist, Plays = g.Sum(x => x.Plays), Duration = g.Sum(x => x.Duration ?? 0) })
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.Artist)
            .Take(take)
            .Select((x, i) => new RankedItem(x.ArtistId, x.Artist, null, x.Plays, x.Duration, i + 1, 0, x.Plays, null, false))
            .ToList();
        return new AudioMatchRanking(playMap, tracks, artists);
    }

    private static AudioAnalyticsResult EmptyAudio() =>
        new(0, 0, 0, 0, 0, null, null, null, null, null, null, null, null, [], [], [], [], [], [], []);

    private static void AddMood(List<NamedAverage> list, string name, double? value)
    {
        if (value is double v)
        {
            list.Add(new NamedAverage(name, v));
        }
    }

    private static List<NamedCount> NamedCounts(
        Dictionary<string, (int Tracks, int Plays)> map,
        IReadOnlyList<string> order)
    {
        return order
            .Where(map.ContainsKey)
            .Select(name => new NamedCount(name, map[name].Tracks, map[name].Plays))
            .ToList();
    }

    private static List<TagStat> RollupAudioLabels(
        IReadOnlyList<TrackAudioProfile> profiles,
        AudioLabelKind kind,
        Dictionary<long, TrackPlayRow> playMap,
        int take)
    {
        return profiles
            .SelectMany(p => p.Labels.Where(l => l.Kind == kind).Select(l => (p.TrackId, l.Name)))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ids = g.Select(x => x.TrackId).Distinct().ToList();
                var plays = ids.Sum(id => playMap.GetValueOrDefault(id)?.Plays ?? 0);
                var duration = ids.Sum(id => playMap.GetValueOrDefault(id)?.Duration ?? 0);
                var display = g.OrderBy(x => x.Name).First().Name;
                return new TagStat(display, plays, ids.Count, duration, ["Essentia"]);
            })
            .OrderByDescending(t => t.Plays)
            .ThenBy(t => t.Name)
            .Take(take)
            .ToList();
    }

    private static List<AudioGenreFolder> RollupGenreFolders(
        IReadOnlyList<TrackAudioProfile> profiles,
        Dictionary<long, TrackPlayRow> playMap)
    {
        var rows = profiles
            .SelectMany(p => p.Labels
                .Where(l => l.Kind == AudioLabelKind.Genre)
                .Select(l =>
                {
                    var split = AudioGenrePath.Split(l.Name);
                    return (p.TrackId, split.Primary, split.Sub);
                })
                .Where(x => !string.IsNullOrEmpty(x.Primary)))
            .ToList();

        return rows
            .GroupBy(x => x.Primary, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var primary = g.OrderBy(x => x.Primary).First().Primary;
                var children = g
                    .Where(x => x.Sub is not null)
                    .GroupBy(x => x.Sub!, StringComparer.OrdinalIgnoreCase)
                    .Select(c => FolderFromGroup(c.Key, primary, c.Select(x => x.TrackId), playMap))
                    .OrderByDescending(c => c.Plays)
                    .ThenBy(c => c.Name)
                    .ToList();
                var folder = FolderFromGroup(primary, null, g.Select(x => x.TrackId), playMap);
                return folder with { Children = children };
            })
            .OrderByDescending(f => f.Plays)
            .ThenBy(f => f.Name)
            .ToList();
    }

    private static AudioGenreFolder FolderFromGroup(
        string name,
        string? parent,
        IEnumerable<long> trackIds,
        Dictionary<long, TrackPlayRow> playMap)
    {
        var ids = trackIds.Distinct().ToList();
        var plays = ids.Sum(id => playMap.GetValueOrDefault(id)?.Plays ?? 0);
        var duration = ids.Sum(id => playMap.GetValueOrDefault(id)?.Duration ?? 0);
        var path = parent is null ? name : AudioGenrePath.Join(parent, name);
        return new AudioGenreFolder(name, parent, path, plays, ids.Count, duration, []);
    }

    private enum TopKind { Artist, Track, Album }

    private sealed record IdNameCount(long Id, string Name, string? Subtitle, int Plays, long? DurationMs);

    private sealed record PlayRow(long UnixTimestamp, long TrackId, int? DurationMs, string Artist, string Title);

    private sealed record TrackPlayRow(long TrackId, int Plays, long Duration);

    private sealed record AudioMatchRanking(
        Dictionary<long, TrackPlayRow> PlayMap,
        IReadOnlyList<RankedItem> Tracks,
        IReadOnlyList<RankedItem> Artists);

    private sealed record TagLinkRow(long TrackId, string Name, string NormalizedName, EnrichmentSource Source, int Weight);
}
