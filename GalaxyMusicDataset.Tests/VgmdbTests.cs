using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Normalization;
using GalaxyMusicDataset.Services.Vgmdb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Tests;

public class VgmdbTests
{
    private const string SearchJson = """
        {
          "query": "FINAL FANTASY VIII",
          "results": {
            "albums": [
              {
                "link": "album/79",
                "catalog": "SSCX-10037",
                "category": "Game",
                "release_date": "1999-11-20",
                "media_format": "CD",
                "titles": {
                  "en": "FITHOS LUSEC WECOS VINOSEC: FINAL FANTASY VIII [Limited Edition]",
                  "ja": "FITHOS LUSEC WECOS VINOSEC: FINAL FANTASY VIII [Limited Edition]"
                }
              },
              {
                "link": "album/999",
                "catalog": "UNRELATED",
                "titles": { "en": "Unrelated Jazz Compilation" }
              }
            ]
          }
        }
        """;

    private const string AlbumJson = """
        {
          "link": "album/79",
          "name": "FITHOS LUSEC WECOS VINOSEC: FINAL FANTASY VIII [Limited Edition]",
          "names": {
            "en": "FITHOS LUSEC WECOS VINOSEC: FINAL FANTASY VIII [Limited Edition]",
            "ja": "FITHOS LUSEC WECOS VINOSEC: FINAL FANTASY VIII [Limited Edition]"
          },
          "catalog": "SSCX-10037",
          "classification": "Arrangement",
          "release_date": "1999-11-20",
          "picture_small": "https://medium-media.vgm.io/albums/97/79/79-cover.png",
          "picture_full": "https://media.vgm.io/albums/97/79/79-cover.png",
          "category": "Game",
          "categories": ["Game"],
          "platforms": ["Sony PlayStation"],
          "products": [
            {
              "link": "product/189",
              "names": { "en": "Final Fantasy VIII", "ja": "ファイナルファンタジーVIII" }
            }
          ],
          "composers": [{ "names": { "en": "Nobuo Uematsu" }, "link": "artist/1" }],
          "discs": [
            {
              "name": "Disc 1",
              "disc_length": "64:16",
              "tracks": [
                {
                  "names": { "English": "Liberi Fatali", "Japanese": "リベリ・ファタリ" },
                  "track_length": "3:09"
                },
                {
                  "names": { "English": "Eyes On Me", "Romaji": "Eyes On Me" },
                  "track_length": "5:36"
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void ParseSearch_reads_album_id_and_titles()
    {
        var (items, _) = VgmdbClient.ParseSearch(SearchJson);
        Assert.Equal(2, items.Count);
        Assert.Equal("79", items[0].Id);
        Assert.Equal("SSCX-10037", items[0].Catalog);
        Assert.Contains("FITHOS LUSEC WECOS VINOSEC: FINAL FANTASY VIII [Limited Edition]", items[0].Titles);
    }

    [Fact]
    public void AlbumIdFromLink_takes_last_segment()
    {
        Assert.Equal("79", VgmdbClient.AlbumIdFromLink("album/79"));
        Assert.Equal("79", VgmdbClient.AlbumIdFromLink("/album/79"));
        Assert.Equal("79", VgmdbClient.AlbumIdFromLink("https://vgmdb.info/album/79"));
        Assert.Null(VgmdbClient.AlbumIdFromLink(" "));
    }

    [Fact]
    public void ParseAlbum_reads_catalog_cover_tracks_and_products()
    {
        var album = VgmdbClient.ParseAlbum(AlbumJson);
        Assert.NotNull(album);
        Assert.Equal("79", album!.Id);
        Assert.Equal("SSCX-10037", album.Catalog);
        Assert.Equal("Arrangement", album.Classification);
        Assert.Equal(1999, album.ReleaseYear);
        Assert.Equal("https://medium-media.vgm.io/albums/97/79/79-cover.png", album.CoverUrl);
        Assert.Contains("Game", album.Categories);
        Assert.Contains("Sony PlayStation", album.Platforms);
        Assert.Contains("Final Fantasy VIII", album.ProductNames);
        Assert.Contains("ファイナルファンタジーVIII", album.ProductNames);
        Assert.Single(album.Discs);
        Assert.Equal(2, album.Discs[0].Tracks.Count);
        Assert.Equal(189000, album.Discs[0].Tracks[0].DurationMs);
        Assert.Contains("Liberi Fatali", album.Discs[0].Tracks[0].Names);
    }

    [Fact]
    public void TagPairs_uses_category_platform_product_not_composers()
    {
        var tags = VgmdbClient.TagPairs(VgmdbClient.ParseAlbum(AlbumJson)!);
        Assert.Contains(tags, t => t.Name == "Game" && t.Weight == 50);
        Assert.Contains(tags, t => t.Name == "Arrangement" && t.Weight == 50);
        Assert.Contains(tags, t => t.Name == "Sony PlayStation" && t.Weight == 40);
        Assert.Contains(tags, t => t.Name == "Final Fantasy VIII" && t.Weight == 45);
        Assert.DoesNotContain(tags, t => t.Name.Contains("Uematsu", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("3:09", 189000)]
    [InlineData("5:36", 336000)]
    [InlineData("64:16", 3856000)]
    [InlineData("1:02:03", 3723000)]
    [InlineData("Unknown", null)]
    [InlineData("0:00", null)]
    [InlineData("", null)]
    public void ParseDurationMs(string raw, int? expected) =>
        Assert.Equal(expected, VgmdbClient.ParseDurationMs(raw));

    [Fact]
    public void PickBestAlbum_accepts_matching_title_and_rejects_unrelated()
    {
        var hits = VgmdbClient.ParseSearch(SearchJson).Items;
        var best = VgmdbAlbumMatcher.PickBestAlbum(
            "Nobuo Uematsu",
            "Liberi Fatali",
            "FITHOS LUSEC WECOS VINOSEC: FINAL FANTASY VIII [Limited Edition]",
            hits);
        Assert.NotNull(best);
        Assert.Equal("79", best!.Id);

        Assert.Null(VgmdbAlbumMatcher.PickBestAlbum(
            "Nobuo Uematsu",
            "Liberi Fatali",
            "Completely Different Album Name That Should Not Match",
            hits));
    }

    [Fact]
    public void PickBestAlbum_skips_generic_ost_without_artist_overlap()
    {
        var hits = VgmdbClient.ParseSearch(SearchJson).Items;
        Assert.True(VgmdbAlbumMatcher.IsGenericAlbumTitle("OST"));
        Assert.True(VgmdbAlbumMatcher.IsGenericAlbumTitle("Original Soundtrack"));
        Assert.Null(VgmdbAlbumMatcher.PickBestAlbum(
            "Some Random Composer",
            "Liberi Fatali",
            "OST",
            hits));
    }

    [Fact]
    public void PickBestTrack_matches_english_and_japanese_names()
    {
        var album = VgmdbClient.ParseAlbum(AlbumJson)!;
        var english = VgmdbAlbumMatcher.PickBestTrack("Liberi Fatali", album);
        Assert.NotNull(english);
        Assert.Equal(189000, english!.DurationMs);

        var japanese = VgmdbAlbumMatcher.PickBestTrack("リベリ・ファタリ", album);
        Assert.NotNull(japanese);
        Assert.Contains("Liberi Fatali", japanese!.Names);

        Assert.Null(VgmdbAlbumMatcher.PickBestTrack("A Song That Is Not On This Album", album));
    }

    [Fact]
    public void SearchQuery_prefers_specific_album_title()
    {
        Assert.Equal(
            "FINAL FANTASY VIII Original Soundtrack",
            VgmdbAlbumMatcher.SearchQuery("Nobuo Uematsu", "Liberi Fatali", "FINAL FANTASY VIII Original Soundtrack"));
        Assert.Equal(
            "Liberi Fatali",
            VgmdbAlbumMatcher.SearchQuery("Nobuo Uematsu", "Liberi Fatali", "OST"));
        Assert.Equal(
            "Liberi Fatali",
            VgmdbAlbumMatcher.SearchQuery("Nobuo Uematsu", "Liberi Fatali", null));
    }

    [Fact]
    public void CoverArtResolver_prefers_vgmdb_over_discogs()
    {
        var cover = CoverArtResolver.CoverFromPayloads(
        [
            (EnrichmentSource.Discogs, """{"id":99,"images":[{"type":"primary","uri150":"https://example.com/discogs.jpg"}]}"""),
            (EnrichmentSource.Vgmdb, AlbumJson)
        ]);
        Assert.Equal("https://medium-media.vgm.io/albums/97/79/79-cover.png", cover);
    }

    [Fact]
    public async Task ApplyVgmdb_fills_id_duration_catalog_year_cover_and_tags()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Nobuo Uematsu", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();
        var track = new Track
        {
            ArtistId = artist.Id,
            Title = "Liberi Fatali",
            Fingerprint = TrackFingerprint.Compute("Nobuo Uematsu", "Liberi Fatali"),
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

        var album = VgmdbClient.ParseAlbum(AlbumJson)!;
        var matched = VgmdbAlbumMatcher.PickBestTrack(track.Title, album)!;
        await service.ApplyVgmdbAsync(track, album, matched, CancellationToken.None);
        await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(CancellationToken.None);

        var saved = await harness.Db.Tracks.Include(t => t.Album).SingleAsync(t => t.Id == track.Id);
        Assert.Equal("79", saved.VgmdbAlbumId);
        Assert.Equal(189000, saved.DurationMs);
        Assert.NotNull(saved.Album);
        Assert.Equal("SSCX-10037", saved.Album!.CatalogNumber);
        Assert.Equal("Arrangement", saved.Album.Classification);
        Assert.Equal(1999, saved.Album.ReleaseYear);
        Assert.Equal("https://medium-media.vgm.io/albums/97/79/79-cover.png", saved.Album.CoverUrl);
        var tags = await harness.Db.TrackTags.Where(t => t.TrackId == track.Id).Include(t => t.Tag).ToListAsync();
        Assert.Contains(tags, t => t.Tag.Name == "Game" && t.Source == EnrichmentSource.Vgmdb);
        Assert.Contains(tags, t => t.Tag.Name == "Final Fantasy VIII");
        Assert.DoesNotContain(tags, t => t.Tag.Name.Contains("Uematsu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Coverage_includes_vgmdb_row()
    {
        var rows = CatalogCoverage.Build(
            10,
            withMbid: 5,
            withDuration: 7,
            withTags: 3,
            withAudio: 1,
            [new(nameof(EnrichmentSource.Vgmdb), nameof(SourceFetchStatus.Success), 2)],
            new AggregationOptions { EnableVgmdb = false },
            lastFmConfigured: true,
            discogsConfigured: true,
            audioDbConfigured: true);
        var vgmdb = rows.Single(r => r.Name == "VGMdb");
        Assert.Equal("off", vgmdb.Note);
        Assert.False(vgmdb.Enabled);
        Assert.Equal(2, vgmdb.Hits);
    }

    [Fact]
    public void BuildAlbumSearchUrl_encodes_query()
    {
        Assert.Equal(
            "https://vgmdb.info/search/albums?q=FINAL%20FANTASY%20VIII&format=json",
            VgmdbClient.BuildAlbumSearchUrl("https://vgmdb.info", "FINAL FANTASY VIII"));
        Assert.Equal(
            "https://vgmdb.info/album/79?format=json",
            VgmdbClient.BuildAlbumUrl("https://vgmdb.info/", "79"));
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
