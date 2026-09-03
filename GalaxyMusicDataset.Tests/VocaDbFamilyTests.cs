using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Pages;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Normalization;
using GalaxyMusicDataset.Services.VocaDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Tests;

public class VocaDbFamilyTests
{
    private const string WorldIsMineJson = """
        {
          "items": [
            {
              "id": 48,
              "name": "ワールドイズマイン",
              "defaultName": "ワールドイズマイン",
              "additionalNames": "World is Mine",
              "artistString": "ryo feat. 初音ミク",
              "songType": "Original",
              "lengthSeconds": 175,
              "thumbUrl": "https://example.com/thumb.jpg",
              "publishDate": "2008-05-31T00:00:00",
              "names": [
                { "language": "Japanese", "value": "ワールドイズマイン" },
                { "language": "English", "value": "World is Mine" }
              ],
              "artists": [
                {
                  "name": "ryo",
                  "categories": "Producer",
                  "artist": { "name": "ryo", "additionalNames": "supercell" }
                },
                {
                  "name": "初音ミク",
                  "categories": "Vocalist",
                  "artist": { "name": "初音ミク", "additionalNames": "Hatsune Miku" }
                }
              ],
              "tags": [
                { "count": 42, "tag": { "name": "Pop", "categoryName": "Genres" } },
                { "count": 10, "tag": { "name": "Vocaloid", "categoryName": "Vocalists" } }
              ],
              "pvs": [
                { "service": "Youtube", "url": "https://www.youtube.com/watch?v=abc", "pvType": "Original", "disabled": false }
              ],
              "webLinks": [
                { "description": "MusicBrainz", "url": "https://musicbrainz.org/recording/11111111-2222-3333-4444-555555555555" }
              ],
              "albums": [{ "name": "supercell" }]
            }
          ]
        }
        """;

    [Fact]
    public void Parse_search_reads_tags_artists_video_and_mbid()
    {
        var (items, _) = VocaDbClient.ParseSearch(WorldIsMineJson);
        var hit = Assert.Single(items);
        Assert.Equal("48", hit.Id);
        Assert.Equal(175, hit.LengthSeconds);
        Assert.Equal("https://example.com/thumb.jpg", hit.ThumbUrl);
        Assert.Equal("https://www.youtube.com/watch?v=abc", hit.MusicVideoUrl);
        Assert.Equal("11111111-2222-3333-4444-555555555555", hit.MusicBrainzId);
        Assert.Equal("supercell", hit.AlbumTitle);
        Assert.Equal(2008, hit.ReleaseYear);
        Assert.Contains("World is Mine", hit.AllTitles);
        Assert.Contains(hit.Artists, a => a.AllNames.Contains("Hatsune Miku"));
        var pairs = VocaDbClient.TagPairs(hit);
        Assert.Contains(pairs, t => t.Name == "Pop" && t.Weight == 80);
        Assert.Contains(pairs, t => t.Name == "Vocaloid" && t.Weight == 10);
    }

    [Fact]
    public void Empty_search_is_empty()
    {
        var (items, _) = VocaDbClient.ParseSearch("""{"items":[]}""");
        Assert.Empty(items);
    }

    [Fact]
    public void Vocalist_as_lastfm_artist_auto_matches()
    {
        var hit = VocaDbClient.ParseSearch(WorldIsMineJson).Items[0];
        var picked = VocaDbSongMatcher.PickBest("Hatsune Miku", "World is Mine", [hit]);
        Assert.NotNull(picked);
        Assert.Equal("48", picked!.Id);
    }

    [Fact]
    public void Weak_title_is_not_found()
    {
        var hit = VocaDbClient.ParseSearch(WorldIsMineJson).Items[0];
        Assert.Null(VocaDbSongMatcher.PickBest("Hatsune Miku", "Completely Different Song", [hit]));
    }

    [Fact]
    public async Task ApplyVocaDb_sets_id_duration_tags_and_aliases()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Hatsune Miku", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();
        var track = new Track
        {
            ArtistId = artist.Id,
            Title = "World is Mine",
            Fingerprint = TrackFingerprint.Compute("Hatsune Miku", "World is Mine"),
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.Add(track);
        await harness.Db.SaveChangesAsync();
        await harness.Db.Entry(track).Reference(t => t.Artist).LoadAsync();

        var catalog = new CatalogService(harness.Db);
        var service = new MetadataEnrichmentService(
            harness.Db,
            null!,
            new TagService(harness.Db),
            catalog,
            new AggregationProgress(),
            new EnrichmentSourceHealth(),
            new StaticMonitor<AggregationOptions>(new AggregationOptions()));

        var hit = VocaDbClient.ParseSearch(WorldIsMineJson).Items[0];
        await service.ApplyVocaDbAsync(track, EnrichmentSource.VocaDb, hit, CancellationToken.None);
        await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(CancellationToken.None);

        var saved = await harness.Db.Tracks.Include(t => t.Artist).ThenInclude(a => a.Aliases)
            .SingleAsync(t => t.Id == track.Id);
        Assert.Equal("48", saved.VocaDbSongId);
        Assert.Equal(175000, saved.DurationMs);
        Assert.Equal("11111111-2222-3333-4444-555555555555", saved.Mbid);
        Assert.Equal("https://www.youtube.com/watch?v=abc", saved.MusicVideoUrl);
        Assert.NotNull(saved.AlbumId);
        Assert.Equal(2, await harness.Db.TrackTags.CountAsync(t => t.TrackId == track.Id));
        Assert.Contains(saved.Artist.Aliases, a => a.Name == "初音ミク");
        Assert.True(AnalyticsQueries.IsGenreLike(EnrichmentSource.VocaDb, 80));
        Assert.False(AnalyticsQueries.IsGenreLike(EnrichmentSource.VocaDb, 10));
    }

    [Fact]
    public void Coverage_uses_success_payloads_and_mbid_field()
    {
        var payloads = new List<SourcePayloadCount>
        {
            new(nameof(EnrichmentSource.VocaDb), nameof(SourceFetchStatus.Success), 2),
            new(nameof(EnrichmentSource.VocaDb), nameof(SourceFetchStatus.NotFound), 3),
            new(nameof(EnrichmentSource.LastFm), nameof(SourceFetchStatus.Success), 4)
        };
        var rows = CatalogCoverage.Build(
            10,
            withMbid: 5,
            withDuration: 7,
            withTags: 3,
            payloads,
            new AggregationOptions(),
            lastFmConfigured: true,
            discogsConfigured: false,
            audioDbConfigured: false);

        var mbid = rows.Single(r => r.Name == "MusicBrainz");
        Assert.Equal(50, mbid.HitPercent);
        Assert.Equal(5, mbid.Hits);
        Assert.False(mbid.ShowAttempted);

        var voca = rows.Single(r => r.Name == "VocaDB");
        Assert.Equal(20, voca.HitPercent);
        Assert.Equal(2, voca.Hits);
        Assert.Equal(5, voca.Attempted);
        Assert.True(voca.ShowAttempted);

        var tags = rows.Single(r => r.Name == "Tags");
        Assert.Equal(3, tags.Hits);
        Assert.Equal(30, tags.HitPercent);

        var discogs = rows.Single(r => r.Name == "Discogs");
        Assert.Equal("no token", discogs.Note);
        Assert.False(discogs.Enabled);
    }

    [Fact]
    public async Task Library_hasTags_filters_tracks()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "A", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();
        var tagged = new Track
        {
            ArtistId = artist.Id,
            Title = "Tagged",
            Fingerprint = "fp-tagged",
            CreatedAt = now,
            UpdatedAt = now
        };
        var bare = new Track
        {
            ArtistId = artist.Id,
            Title = "Bare",
            Fingerprint = "fp-bare",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.AddRange(tagged, bare);
        await harness.Db.SaveChangesAsync();
        var tag = new Tag { Name = "j-pop", NormalizedName = "j-pop" };
        harness.Db.Tags.Add(tag);
        await harness.Db.SaveChangesAsync();
        harness.Db.TrackTags.Add(new TrackTag
        {
            TrackId = tagged.Id,
            TagId = tag.Id,
            Source = EnrichmentSource.VocaDb,
            Weight = 80
        });
        await harness.Db.SaveChangesAsync();

        var yes = await LibraryFilters.Apply(harness.Db.Tracks, harness.Db, null, null, null, null, null, "yes", null, null)
            .Select(t => t.Title)
            .ToListAsync();
        var no = await LibraryFilters.Apply(harness.Db.Tracks, harness.Db, null, null, null, null, null, "no", null, null)
            .Select(t => t.Title)
            .ToListAsync();
        Assert.Equal(["Tagged"], yes);
        Assert.Equal(["Bare"], no);
    }
}

file sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable OnChange(Action<T, string?> listener) => new Noop();

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
