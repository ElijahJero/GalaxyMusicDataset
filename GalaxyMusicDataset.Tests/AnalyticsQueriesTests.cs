using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class AnalyticsQueriesTests
{
    private static readonly DateTimeOffset Now = new(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TimeRange_presets_and_previous_window()
    {
        var week = TimeRangeParser.Parse("7d", null, null, Now);
        Assert.Equal("7d", week.Preset);
        Assert.Equal(Now.AddDays(-7), week.From);

        var custom = TimeRangeParser.Parse("custom", "2024-01-01", "2024-01-07", Now);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), custom.From);
        Assert.Equal(new DateTimeOffset(2024, 1, 8, 0, 0, 0, TimeSpan.Zero), custom.To);

        var previous = TimeRangeParser.PreviousWindow(custom);
        Assert.Equal(new DateTimeOffset(2023, 12, 25, 0, 0, 0, TimeSpan.Zero), previous.From);
        Assert.Equal(custom.From, previous.To);
    }

    [Fact]
    public void Streak_walks_back_from_today_or_yesterday()
    {
        var days = new[]
        {
            new DateOnly(2024, 1, 10),
            new DateOnly(2024, 1, 11),
            new DateOnly(2024, 1, 12),
            new DateOnly(2024, 1, 14)
        };
        var today = AnalyticsQueries.ComputeStreak(days, new DateOnly(2024, 1, 15));
        Assert.Equal(1, today.Current);
        Assert.Equal(3, today.Longest);
        Assert.Equal(new DateOnly(2024, 1, 14), today.CurrentEnd);

        var yesterday = AnalyticsQueries.ComputeStreak(days, new DateOnly(2024, 1, 14));
        Assert.Equal(1, yesterday.Current);
        Assert.Equal(new DateOnly(2024, 1, 14), yesterday.CurrentStart);
    }

    [Fact]
    public async Task Overview_counts_listening_time_and_search_aliases()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var range = TimeRangeParser.ForCalendarYear(2024);

        var overview = await queries.GetOverview(range, null, CancellationToken.None, includeAllTime: false, Now);
        Assert.Equal(8, overview.ScrobbleCount);
        Assert.Equal(4, overview.UniqueTracks);
        Assert.Equal(2, overview.UniqueArtists);
        Assert.Equal(2, overview.UniqueAlbums);
        Assert.Equal(180_000L * 6, overview.ListeningTimeMs);
        Assert.Equal(2, overview.PlaysMissingDuration);
        Assert.Equal(4, overview.DaysTrackedAllTime);
        Assert.NotNull(overview.MostRecent);
        Assert.Equal("INSOMNIAC BLACK", overview.MostRecent.Title);

        var aliasHit = await queries.GetOverview(range, "mori", CancellationToken.None, includeAllTime: false, Now);
        Assert.Equal(7, aliasHit.ScrobbleCount);
        Assert.Equal(1, aliasHit.UniqueArtists);

        var heatmap = await queries.GetHeatmap(range, null, CancellationToken.None);
        var mondayMorning = heatmap.Cells.Single(c => c.WeekdayMonday0 == 0 && c.HourUtc == 10);
        Assert.Equal(2, mondayMorning.Count);

        var buckets = queries.GetTimeOfDayBuckets(heatmap);
        Assert.Equal(4, buckets.Single(b => b.Name == "Morning").Count);
    }

    [Fact]
    public async Task Tops_mark_movers_and_new_entries()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var current = TimeRangeParser.Parse("custom", "2024-01-08", "2024-01-14", Now);
        var previous = TimeRangeParser.PreviousWindow(current);

        var artists = await queries.GetTopArtists(current, previous, null, 10, CancellationToken.None);
        var calliope = artists.Items.Single(i => i.Name == "Mori Calliope");
        Assert.False(calliope.IsNew);
        Assert.Equal(2, calliope.Plays);
        Assert.Equal(5, calliope.PreviousPlays);
        Assert.Equal(-3, calliope.Delta);

        var tracks = await queries.GetTopTracks(current, previous, null, 10, CancellationToken.None);
        Assert.Contains(tracks.Items, t => t.Name == "Way 2 U" && t.IsNew);
    }

    [Fact]
    public async Task Discovery_is_first_heard_in_range()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var range = TimeRangeParser.Parse("custom", "2024-01-08", "2024-01-14", Now);
        var result = await queries.GetDiscoveries(range, null, 50, CancellationToken.None);

        Assert.Contains(result.Tracks, t => t.Name == "Way 2 U");
        Assert.DoesNotContain(result.Tracks, t => t.Name == "Lose-Lose Days");
        Assert.Contains(result.Artists, a => a.Name == "Ouro Kronii");
        Assert.DoesNotContain(result.Artists, a => a.Name == "Mori Calliope");
    }

    [Fact]
    public async Task Deep_cuts_split_one_offs_from_heavy()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var range = TimeRangeParser.ForCalendarYear(2024);
        var cuts = await queries.GetDeepCuts(range, null, 3, 50, CancellationToken.None);

        Assert.Contains(cuts.OneOffs, t => t.Name == "Way 2 U");
        Assert.Contains(cuts.Heavy, t => t.Name == "Lose-Lose Days" && t.Plays == 4);
        Assert.DoesNotContain(cuts.OneOffs, t => t.Name == "Lose-Lose Days");
    }

    [Fact]
    public async Task Sessions_cluster_repeats_and_skip_adjacent()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var range = TimeRangeParser.ForCalendarYear(2024);
        var sessions = await queries.GetSessions(range, null, 30, 50, CancellationToken.None);

        Assert.True(sessions.SessionCount >= 3);
        Assert.True(sessions.RepeatRate > 0);
        Assert.Equal(1, sessions.SkipAdjacentCount);
        Assert.Contains(sessions.Sessions, s => s.TrackCount >= 2);
    }

    [Fact]
    public async Task Artist_and_track_detail_round_trip()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var artist = await harness.Db.Artists.SingleAsync(a => a.Name == "Mori Calliope");
        var detail = await queries.GetArtistDetail(artist.Id, TimeRangeParser.ForCalendarYear(2024), null, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Contains("Calliope Mori", detail.Aliases);
        Assert.Equal(7, detail.PlayCount);
        Assert.Contains(detail.TopTracks, t => t.Name == "Lose-Lose Days");

        var track = await harness.Db.Tracks.SingleAsync(t => t.Title == "Lose-Lose Days");
        var trackDetail = await queries.GetTrackDetail(track.Id, CancellationToken.None);
        Assert.NotNull(trackDetail);
        Assert.Equal(4, trackDetail.PlayCount);
        Assert.Equal(4, trackDetail.PlayedAt.Count);
    }

    [Fact]
    public async Task Wrapped_constrains_to_calendar_year()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var wrapped = await queries.GetWrapped(2024, null, CancellationToken.None);
        Assert.Equal(2024, wrapped.Year);
        Assert.Equal(8, wrapped.Overview.ScrobbleCount);
        Assert.NotNull(wrapped.MostReplayed);
        Assert.Equal("Lose-Lose Days", wrapped.MostReplayed.Name);
        Assert.Contains(wrapped.NewArtists, a => a.Name == "Ouro Kronii");
        Assert.True(wrapped.LongestStreak >= 2);
        Assert.Contains(wrapped.TopGenres, g => g.Name == "hip hop");
    }

    [Fact]
    public async Task Tag_cloud_ranks_genres_by_plays_and_dedupes_sources()
    {
        await using var harness = await SeedAsync();
        var queries = new AnalyticsQueries(harness.Db);
        var range = TimeRangeParser.ForCalendarYear(2024);
        var cloud = await queries.GetTagCloud(range, null, 20, CancellationToken.None);

        var hipHop = cloud.Genres.Single(t => t.Name == "hip hop");
        Assert.Equal(4, hipHop.Plays);
        Assert.Equal(1, hipHop.TrackCount);
        Assert.Contains("MusicBrainz", hipHop.Sources);
        Assert.Contains("LastFm", cloud.Tags.Single(t => t.Name == "hip hop").Sources);

        var seenLive = cloud.Tags.Single(t => t.Name == "seen live");
        Assert.Equal(1, seenLive.Plays);
        Assert.DoesNotContain(cloud.Genres, t => t.Name == "seen live");

        var detail = await queries.GetTagDetail("hip hop", range, null, 20, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Contains(detail.Tracks, t => t.Name == "Lose-Lose Days");
        Assert.Contains(detail.Artists, a => a.Name == "Mori Calliope");
    }

    private static async Task<TestDb> SeedAsync()
    {
        var harness = await TestDb.CreateAsync();
        var catalog = new CatalogService(harness.Db);
        var calliope = await catalog.GetOrCreateArtistAsync("Mori Calliope", null, CancellationToken.None);
        harness.Db.ArtistAliases.Add(new ArtistAlias
        {
            ArtistId = calliope.Id,
            Name = "Calliope Mori",
            Locale = "en",
            Source = "MusicBrainz"
        });
        var kronii = await catalog.GetOrCreateArtistAsync("Ouro Kronii", null, CancellationToken.None);
        var unalive = await catalog.GetOrCreateAlbumAsync(calliope, "UnAlive", null, CancellationToken.None);
        var wayAlbum = await catalog.GetOrCreateAlbumAsync(kronii, "Way 2 U", null, CancellationToken.None);

        var lose = await AddTrack(harness.Db, calliope, unalive, "Lose-Lose Days", 180_000);
        var lullaby = await AddTrack(harness.Db, calliope, unalive, "Left For Dead Lullaby", 180_000);
        var way = await AddTrack(harness.Db, kronii, wayAlbum, "Way 2 U", 180_000);
        var untitled = await AddTrack(harness.Db, calliope, null, "INSOMNIAC BLACK", null);

        var hipHop = new Tag { Name = "hip hop", NormalizedName = "hip hop" };
        var seenLive = new Tag { Name = "seen live", NormalizedName = "seen live" };
        harness.Db.Tags.AddRange(hipHop, seenLive);
        await harness.Db.SaveChangesAsync();
        harness.Db.TrackTags.AddRange(
            new TrackTag { TrackId = lose.Id, TagId = hipHop.Id, Source = EnrichmentSource.MusicBrainz, Weight = 80 },
            new TrackTag { TrackId = lose.Id, TagId = hipHop.Id, Source = EnrichmentSource.LastFm, Weight = 12 },
            new TrackTag { TrackId = way.Id, TagId = seenLive.Id, Source = EnrichmentSource.LastFm, Weight = 8 });
        await harness.Db.SaveChangesAsync();

        // Monday 2024-01-01 10:00 UTC — two plays of Lose-Lose Days, 20s apart (skip-adjacent)
        await AddPlay(harness.Db, lose, Unix(2024, 1, 1, 10, 0));
        await AddPlay(harness.Db, lose, Unix(2024, 1, 1, 10, 0) + 20);
        // Tuesday cluster (same session) + one later play
        await AddPlay(harness.Db, lullaby, Unix(2024, 1, 2, 18, 0));
        await AddPlay(harness.Db, lose, Unix(2024, 1, 2, 18, 4));
        await AddPlay(harness.Db, untitled, Unix(2024, 1, 2, 19, 10));
        // Thursday new artist
        await AddPlay(harness.Db, way, Unix(2024, 1, 11, 21, 0));
        // Friday repeats of Lose-Lose Days
        await AddPlay(harness.Db, lose, Unix(2024, 1, 12, 9, 0));
        await AddPlay(harness.Db, untitled, Unix(2024, 1, 12, 9, 5));
        await harness.Db.SaveChangesAsync();
        return harness;
    }

    private static async Task<Track> AddTrack(AppDbContext db, Artist artist, Album? album, string title, int? durationMs)
    {
        var track = new Track
        {
            ArtistId = artist.Id,
            AlbumId = album?.Id,
            Title = title,
            DurationMs = durationMs,
            Fingerprint = title,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track;
    }

    private static async Task AddPlay(AppDbContext db, Track track, long unix)
    {
        db.Scrobbles.Add(new Scrobble
        {
            TrackId = track.Id,
            PlayedAt = DateTimeOffset.FromUnixTimeSeconds(unix),
            UnixTimestamp = unix,
            OriginalArtist = track.Title,
            OriginalTitle = track.Title
        });
        await db.SaveChangesAsync();
    }

    private static long Unix(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds();
}
