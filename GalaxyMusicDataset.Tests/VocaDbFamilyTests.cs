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
    public void Song_search_query_is_title_only()
    {
        Assert.Equal("World is Mine", VocaDbClient.SongSearchQuery("World is Mine"));
        Assert.Null(VocaDbClient.SongSearchQuery(" \0\t "));
        var url = VocaDbClient.BuildSongSearchUrl("https://vocadb.net", "World is Mine");
        Assert.Contains("query=World%20is%20Mine", url, StringComparison.Ordinal);
        Assert.Contains("sort=RatingScore", url, StringComparison.Ordinal);
        Assert.DoesNotContain("preferAccurateMatches=true", url, StringComparison.Ordinal);
        Assert.DoesNotContain("Hatsune", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Miku", url, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://vocadb.net/api/songs?", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Combined_title_artist_query_is_not_used_for_search()
    {
        // Regression: stuffing the artist into query makes Auto/Words require those
        // tokens in the song name, so "World is Mine Hatsune Miku" misses id 1326.
        var query = VocaDbClient.SongSearchQuery("World is Mine");
        Assert.Equal("World is Mine", query);
        Assert.DoesNotContain("Hatsune Miku", query, StringComparison.Ordinal);
    }

    [Fact]
    public void PickBest_selects_canonical_song_when_newer_unrelated_hits_come_first()
    {
        var junk = VocaDbClient.ParseSearch("""
            {
              "items": [
                {
                  "id": 885335,
                  "name": "/ ワールドイズマイン",
                  "additionalNames": "",
                  "artistString": "WONKAloid feat. Big Jack Horner (UTAU)",
                  "songType": "Cover",
                  "artists": [{ "name": "WONKAloid", "categories": "Producer" }]
                }
              ]
            }
            """).Items[0];
        var canonical = VocaDbClient.ParseSearch(WorldIsMineJson).Items[0];
        var picked = VocaDbSongMatcher.PickBest("Hatsune Miku", "World is Mine", [junk, canonical]);
        Assert.NotNull(picked);
        Assert.Equal("48", picked!.Id);
    }

    [Fact]
    public void Legacy_not_found_messages_are_false_negatives()
    {
        Assert.True(VocaDbFamily.IsLegacyFalseNegative("VocaDB returned no songs."));
        Assert.True(VocaDbFamily.IsLegacyFalseNegative("UtaiteDB returned no songs."));
        Assert.True(VocaDbFamily.IsLegacyFalseNegative("TouhouDB returned no songs."));
        Assert.True(VocaDbFamily.IsLegacyFalseNegative("No VocaDB-family match passed the auto-match threshold."));
        Assert.False(VocaDbFamily.IsLegacyFalseNegative(VocaDbFamily.NoSongsMessage(EnrichmentSource.VocaDb)));
        Assert.False(VocaDbFamily.IsLegacyFalseNegative(VocaDbFamily.WeakMatchMessage));
        Assert.False(VocaDbFamily.IsLegacyFalseNegative(null));
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
    public async Task Requeue_resets_legacy_not_found_but_keeps_new_not_found()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Hatsune Miku", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();
        var tracks = Enumerable.Range(0, 4).Select(i => new Track
        {
            ArtistId = artist.Id,
            Title = $"Song {i}",
            Fingerprint = $"fp-voca-{i}",
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
        harness.Db.Tracks.AddRange(tracks);
        await harness.Db.SaveChangesAsync();

        harness.Db.TrackSourcePayloads.AddRange(
            new TrackSourcePayload
            {
                TrackId = tracks[0].Id,
                Source = EnrichmentSource.VocaDb,
                Status = SourceFetchStatus.NotFound,
                ErrorMessage = "VocaDB returned no songs.",
                FetchedAt = now,
                PayloadJson = """{"items":[]}"""
            },
            new TrackSourcePayload
            {
                TrackId = tracks[1].Id,
                Source = EnrichmentSource.UtaiteDb,
                Status = SourceFetchStatus.NotFound,
                ErrorMessage = "No VocaDB-family match passed the auto-match threshold.",
                FetchedAt = now
            },
            new TrackSourcePayload
            {
                TrackId = tracks[2].Id,
                Source = EnrichmentSource.TouhouDb,
                Status = SourceFetchStatus.NotFound,
                ErrorMessage = VocaDbFamily.NoSongsMessage(EnrichmentSource.TouhouDb),
                FetchedAt = now
            },
            new TrackSourcePayload
            {
                TrackId = tracks[3].Id,
                Source = EnrichmentSource.VocaDb,
                Status = SourceFetchStatus.Success,
                ExternalId = "1326",
                FetchedAt = now
            });
        await harness.Db.SaveChangesAsync();

        var catalog = new CatalogService(harness.Db);
        var service = new MetadataEnrichmentService(
            harness.Db,
            null!,
            new TagService(harness.Db),
            catalog,
            new AggregationProgress(),
            new EnrichmentSourceHealth(),
            new StaticMonitor<AggregationOptions>(new AggregationOptions()));

        var n = await service.RequeueVocaDbFamilyFalseNegativesAsync(CancellationToken.None);
        Assert.Equal(2, n);

        harness.Db.ChangeTracker.Clear();
        var rows = await harness.Db.TrackSourcePayloads.OrderBy(p => p.TrackId).ToListAsync();
        Assert.Equal(SourceFetchStatus.NotStarted, rows[0].Status);
        Assert.Null(rows[0].ErrorMessage);
        Assert.Null(rows[0].PayloadJson);
        Assert.Equal(SourceFetchStatus.NotStarted, rows[1].Status);
        Assert.Equal(SourceFetchStatus.NotFound, rows[2].Status);
        Assert.Equal(SourceFetchStatus.Success, rows[3].Status);
        Assert.Equal("1326", rows[3].ExternalId);
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

        var yes = await LibraryFilters.Apply(harness.Db.Tracks, harness.Db, null, null, null, null, null, null, "yes", null, null)
            .Select(t => t.Title)
            .ToListAsync();
        var no = await LibraryFilters.Apply(harness.Db.Tracks, harness.Db, null, null, null, null, null, null, "no", null, null)
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
