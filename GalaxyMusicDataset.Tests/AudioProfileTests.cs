using System.Text.Json;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class AudioProfileTests
{
    [Fact]
    public void Mapper_accepts_string_tuple_and_object_labels()
    {
        using var genres = JsonDocument.Parse("""["Pop---J-pop", {"name":"Rock/Pop Rock","score":0.22}, ["electronic", 0.11]]""");
        var labels = AudioProfileMapper.ParseLabels(genres.RootElement, AudioLabelKind.Genre);
        Assert.Contains(labels, l => l.Name == "Pop/J-pop" && l.Score is null);
        Assert.Contains(labels, l => l.Name == "Rock/Pop Rock" && l.Score == 0.22);
        Assert.Contains(labels, l => l.Name == "electronic" && l.Score == 0.11);
    }

    [Fact]
    public void Mapper_splits_key_and_scale()
    {
        Assert.Equal(("G", "major"), AudioProfileMapper.SplitKey("G major", null));
        Assert.Equal(("A#", "minor"), AudioProfileMapper.SplitKey("A#", "minor"));
    }

    [Fact]
    public void Mapper_accepts_analyze_snake_case_json()
    {
        const string json = """
            {
              "bpm": 85,
              "key": "G major",
              "key_strength": 0.72,
              "timbre_bright": 0.22,
              "danceability": 0.93,
              "moods": { "party": 0.95, "happy": 0.78 },
              "genre_scores": [["Pop---J-pop", 0.41], ["Rock/Pop Rock", 0.22]],
              "themes": [["energetic", 0.88]],
              "instruments": [{ "name": "drums", "score": 0.91 }]
            }
            """;
        var request = JsonSerializer.Deserialize<AudioProfileWriteRequest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        Assert.NotNull(request);
        var parsed = AudioProfileMapper.Parse(request);
        Assert.Equal(85, parsed.Bpm);
        Assert.Equal("G", parsed.Key);
        Assert.Equal("major", parsed.Scale);
        Assert.Equal(0.72, parsed.KeyStrength);
        Assert.Equal(0.22, parsed.TimbreBright);
        Assert.Equal(0.95, parsed.MoodParty);
        Assert.Contains(parsed.Genres, g => g.Name == "Pop/J-pop" && g.Score == 0.41);
        Assert.Contains(parsed.Themes, t => t.Name == "energetic" && t.Score == 0.88);
    }

    [Fact]
    public async Task Upsert_does_not_create_track_tags()
    {
        await using var harness = await TestDb.CreateAsync();
        var (track, _) = await SeedTrack(harness);
        var service = new AudioProfileService(harness.Db);
        var request = SampleRequest();

        var view = await service.UpsertAsync(track.Id, request, CancellationToken.None);

        Assert.Equal(0, await harness.Db.TrackTags.CountAsync());
        Assert.Equal(1, await harness.Db.TrackAudioProfiles.CountAsync());
        Assert.Equal(3, await harness.Db.TrackAudioLabels.CountAsync(l => l.Kind == AudioLabelKind.Genre));
        Assert.Equal("G", view.Key);
        Assert.Equal("major", view.Scale);
        Assert.Equal(0.95, view.MoodParty);
        Assert.Contains(view.Genres, g => g.Name == "Pop/J-pop");
        Assert.Contains(view.Themes, t => t.Name == "energetic" && t.Score == 0.88);

        var again = await service.UpsertAsync(track.Id, SampleRequest() with { Bpm = 90 }, CancellationToken.None);
        Assert.Equal(90, again.Bpm);
        Assert.Equal(1, await harness.Db.TrackAudioProfiles.CountAsync());
        Assert.Equal(0, await harness.Db.TrackTags.CountAsync());
    }

    [Fact]
    public async Task Pending_skips_profiled_tracks_and_orders_by_plays()
    {
        await using var harness = await TestDb.CreateAsync();
        var catalog = new CatalogService(harness.Db);
        var artist = await catalog.GetOrCreateArtistAsync("fourfolium", null, CancellationToken.None);
        var shake = await AddNamed(harness.Db, artist, "Shake!");
        var other = await AddNamed(harness.Db, artist, "Other");
        await AddPlay(harness.Db, shake, 1);
        await AddPlay(harness.Db, shake, 2);
        await AddPlay(harness.Db, other, 3);
        await harness.Db.SaveChangesAsync();

        var service = new AudioProfileService(harness.Db);
        var pending = await service.ListPendingAsync(10, CancellationToken.None);
        Assert.Equal(["Shake!", "Other"], pending.Select(p => p.Title).ToList());

        await service.UpsertAsync(shake.Id, SampleRequest(), CancellationToken.None);
        pending = await service.ListPendingAsync(10, CancellationToken.None);
        Assert.Equal(["Other"], pending.Select(p => p.Title).ToList());
    }

    [Fact]
    public async Task Tag_cloud_excludes_essentia_labels()
    {
        await using var harness = await SeedAnalyticsAsync();
        var lose = await harness.Db.Tracks.SingleAsync(t => t.Title == "Lose-Lose Days");
        var service = new AudioProfileService(harness.Db);
        await service.UpsertAsync(lose.Id, SampleRequest(), CancellationToken.None);

        var queries = new AnalyticsQueries(harness.Db);
        var range = TimeRangeParser.ForCalendarYear(2024);
        var cloud = await queries.GetTagCloud(range, null, 20, CancellationToken.None);
        Assert.DoesNotContain(cloud.Genres, t => t.Name.Contains("J-pop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cloud.Tags, t => t.Name == "energetic");

        var audio = await queries.GetAudioAnalytics(range, null, 20, CancellationToken.None);
        Assert.Contains(audio.Genres, t => t.Name == "Pop/J-pop");
        Assert.True(audio.ProfiledPlays > 0);
        Assert.Equal(["Essentia"], audio.Genres.Single(t => t.Name == "Pop/J-pop").Sources);
    }

    [Fact]
    public async Task Merge_keeps_surviving_profile()
    {
        await using var harness = await TestDb.CreateAsync();
        var catalog = new CatalogService(harness.Db);
        var artist = await catalog.GetOrCreateArtistAsync("A", null, CancellationToken.None);
        var keep = await AddNamed(harness.Db, artist, "Same");
        var drop = await AddNamed(harness.Db, artist, "Other");
        var service = new AudioProfileService(harness.Db);
        await service.UpsertAsync(keep.Id, SampleRequest() with { Bpm = 120 }, CancellationToken.None);
        await service.UpsertAsync(drop.Id, SampleRequest() with { Bpm = 80 }, CancellationToken.None);

        await catalog.MergeTracksAsync(keep, drop, CancellationToken.None);

        Assert.Equal(1, await harness.Db.Tracks.CountAsync());
        Assert.Equal(1, await harness.Db.TrackAudioProfiles.CountAsync());
        var profile = await harness.Db.TrackAudioProfiles.SingleAsync();
        Assert.Equal(keep.Id, profile.TrackId);
        Assert.Equal(120, profile.Bpm);
        Assert.Equal(0, await harness.Db.TrackTags.CountAsync());
    }

    [Fact]
    public async Task Upsert_by_fingerprint_matches_artist_and_title()
    {
        await using var harness = await TestDb.CreateAsync();
        var (track, _) = await SeedTrack(harness);
        var service = new AudioProfileService(harness.Db);
        var view = await service.UpsertByNamesAsync("fourfolium", "Shake!", SampleRequest(), CancellationToken.None);
        Assert.Equal(85, view.Bpm);
        Assert.Equal(track.Id, await harness.Db.TrackAudioProfiles.Select(p => p.TrackId).SingleAsync());
    }

    private static async Task<(Track Track, Artist Artist)> SeedTrack(TestDb harness)
    {
        var catalog = new CatalogService(harness.Db);
        var artist = await catalog.GetOrCreateArtistAsync("fourfolium", null, CancellationToken.None);
        var track = await AddNamed(harness.Db, artist, "Shake!");
        return (track, artist);
    }

    private static async Task<Track> AddNamed(AppDbContext db, Artist artist, string title)
    {
        var now = DateTimeOffset.UtcNow;
        var track = new Track
        {
            ArtistId = artist.Id,
            Title = title,
            Fingerprint = TrackFingerprint.Compute(artist.Name, title),
            CreatedAt = now,
            UpdatedAt = now
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

    private static async Task<TestDb> SeedAnalyticsAsync()
    {
        var harness = await TestDb.CreateAsync();
        var catalog = new CatalogService(harness.Db);
        var now = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var calliope = await catalog.GetOrCreateArtistAsync("Mori Calliope", null, CancellationToken.None);
        var lose = new Track
        {
            ArtistId = calliope.Id,
            Title = "Lose-Lose Days",
            DurationMs = 180_000,
            Fingerprint = "lose",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.Add(lose);
        await harness.Db.SaveChangesAsync();
        var hipHop = new Tag { Name = "hip hop", NormalizedName = "hip hop" };
        harness.Db.Tags.Add(hipHop);
        await harness.Db.SaveChangesAsync();
        harness.Db.TrackTags.Add(new TrackTag
        {
            TrackId = lose.Id,
            TagId = hipHop.Id,
            Source = EnrichmentSource.MusicBrainz,
            Weight = 80
        });
        await AddPlay(harness.Db, lose, new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds());
        await harness.Db.SaveChangesAsync();
        return harness;
    }

    private static AudioProfileWriteRequest SampleRequest()
    {
        using var genres = JsonDocument.Parse("""["Pop---J-pop", "Rock/Pop Rock", "Rock/Alternative Rock"]""");
        using var themes = JsonDocument.Parse("""[["energetic", 0.88], ["happy", 0.71]]""");
        using var instruments = JsonDocument.Parse("""[{"name":"drums","score":0.91},{"name":"electricguitar","score":0.64}]""");
        return new AudioProfileWriteRequest
        {
            Bpm = 85,
            Key = "G major",
            KeyStrength = 0.72,
            Loudness = 0.15,
            Danceability = 0.93,
            Acoustic = 0.02,
            Electronic = 0.27,
            Voice = 0.95,
            Instrumental = 0.05,
            Tonal = 0.81,
            Timbre = "dark",
            TimbreBright = 0.22,
            Approachability = 0.72,
            Engagement = 0.84,
            Moods = new Dictionary<string, double>
            {
                ["happy"] = 0.78,
                ["sad"] = 0.05,
                ["aggressive"] = 0.70,
                ["relaxed"] = 0.22,
                ["party"] = 0.95
            },
            Genres = genres.RootElement.Clone(),
            Themes = themes.RootElement.Clone(),
            Instruments = instruments.RootElement.Clone()
        };
    }
}
