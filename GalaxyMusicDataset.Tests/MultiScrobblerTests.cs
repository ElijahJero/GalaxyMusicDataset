using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.ListenBrainz;
using GalaxyMusicDataset.Services.Maloja;
using GalaxyMusicDataset.Services.Scrobbling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Tests;

file sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public class MultiScrobblerTests
{
    [Fact]
    public void Maloja_parses_scrobble_json()
    {
        var json = """
            {
              "time": 1650684324,
              "track": {
                "artists": ["Jennie Kim", "HyunA"],
                "title": "Wow Thing",
                "album": { "albumtitle": "SM Station", "artists": ["SM"] },
                "length": 200
              },
              "duration": 196,
              "origin": "client:navidrome_desktop"
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var play = MalojaClient.ParseScrobble(doc.RootElement);
        Assert.NotNull(play);
        Assert.Equal(ScrobbleProvider.Maloja, play!.Source);
        Assert.Equal(1650684324, play.TimestampUnix);
        Assert.Equal("Jennie Kim, HyunA", play.ArtistName);
        Assert.Equal("Wow Thing", play.TrackName);
        Assert.Equal("SM Station", play.AlbumName);
    }

    [Fact]
    public void ListenBrainz_parses_listen_json()
    {
        var json = """
            {
              "listened_at": 1512040365,
              "track_metadata": {
                "artist_name": "Rick Astley",
                "track_name": "Never Gonna Give You Up",
                "release_name": "Whenever You Need Somebody",
                "additional_info": {
                  "recording_mbid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "artist_mbids": ["11111111-2222-3333-4444-555555555555"],
                  "release_mbid": "99999999-8888-7777-6666-555555555555"
                }
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var play = ListenBrainzClient.ParseListen(doc.RootElement);
        Assert.NotNull(play);
        Assert.Equal(ScrobbleProvider.ListenBrainz, play!.Source);
        Assert.Equal(1512040365, play.TimestampUnix);
        Assert.Equal("Rick Astley", play.ArtistName);
        Assert.Equal("Never Gonna Give You Up", play.TrackName);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", play.TrackMbid);
        Assert.Equal("11111111-2222-3333-4444-555555555555", play.ArtistMbid);
    }

    [Fact]
    public void AggregationOptions_parses_primary_source_aliases()
    {
        Assert.Equal(ScrobbleProvider.Maloja, AggregationOptions.ParseScrobbleProvider("maloja"));
        Assert.Equal(ScrobbleProvider.ListenBrainz, AggregationOptions.ParseScrobbleProvider("listen_brainz"));
        Assert.Equal(ScrobbleProvider.LibreFm, AggregationOptions.ParseScrobbleProvider("libre.fm"));
        Assert.Equal(ScrobbleProvider.LastFm, AggregationOptions.ParseScrobbleProvider("Last.fm"));
    }

    [Fact]
    public async Task Cross_provider_same_timestamp_is_duplicate()
    {
        await using var harness = await TestDb.CreateAsync();
        var options = new StaticOptionsMonitor<AggregationOptions>(new AggregationOptions { CrossProviderMatchSeconds = 2 });
        var ingest = new ScrobbleIngestService(harness.Db, new CatalogService(harness.Db), options);

        var lastFm = await ingest.IngestAsync(
        [
            new IncomingPlay(
                ScrobbleProvider.LastFm,
                "Mori Calliope",
                "Lose-Lose Days",
                "UnAlive",
                1_700_000_100,
                null,
                null,
                null,
                false,
                "{}")
        ], CancellationToken.None);

        var maloja = await ingest.IngestAsync(
        [
            new IncomingPlay(
                ScrobbleProvider.Maloja,
                "Mori Calliope",
                "Lose-Lose Days",
                "UnAlive",
                1_700_000_100,
                null,
                null,
                null,
                false,
                "{}")
        ], CancellationToken.None);

        Assert.Equal(1, lastFm.Inserted);
        Assert.Equal(0, maloja.Inserted);
        Assert.Equal(1, maloja.Duplicates);
        Assert.Equal(1, await harness.Db.Scrobbles.CountAsync());
        Assert.Equal(ScrobbleProvider.LastFm, await harness.Db.Scrobbles.Select(s => s.Source).SingleAsync());
    }

    [Fact]
    public async Task Cross_provider_soft_match_within_tolerance()
    {
        await using var harness = await TestDb.CreateAsync();
        var options = new StaticOptionsMonitor<AggregationOptions>(new AggregationOptions { CrossProviderMatchSeconds = 2 });
        var ingest = new ScrobbleIngestService(harness.Db, new CatalogService(harness.Db), options);

        await ingest.IngestAsync(
        [
            new IncomingPlay(
                ScrobbleProvider.LastFm,
                "fourfolium",
                "Now Loading!!!!",
                null,
                1_700_000_200,
                null,
                null,
                null,
                false,
                "{}")
        ], CancellationToken.None);

        var near = await ingest.IngestAsync(
        [
            new IncomingPlay(
                ScrobbleProvider.Maloja,
                "fourfolium",
                "Now Loading!!!!",
                null,
                1_700_000_201,
                null,
                null,
                null,
                false,
                "{}")
        ], CancellationToken.None);

        Assert.Equal(1, near.Duplicates);
        Assert.Equal(0, near.Inserted);
        Assert.Equal(1, await harness.Db.Scrobbles.CountAsync());
    }

    [Fact]
    public async Task Reconcile_does_not_delete_other_provider_scrobbles()
    {
        await using var harness = await TestDb.CreateAsync();
        var ingest = new ScrobbleIngestService(
            harness.Db,
            new CatalogService(harness.Db),
            new StaticOptionsMonitor<AggregationOptions>(new AggregationOptions { CrossProviderMatchSeconds = 0 }));
        var reconcile = new ScrobbleReconcileService(harness.Db, ingest);
        var (from, to) = ScrobbleReconcile.UtcDayRange(new DateTime(2024, 6, 1));
        var keepLastFm = from + 100;
        var malojaOnly = from + 200;

        await ingest.IngestAsync(
        [
            new IncomingPlay(ScrobbleProvider.LastFm, "A", "Keep", null, keepLastFm, null, null, null, false, "{}"),
            new IncomingPlay(ScrobbleProvider.Maloja, "B", "MalojaPlay", null, malojaOnly, null, null, null, false, "{}")
        ], CancellationToken.None);

        // Maloja reconcile for the day only sees the Maloja play — must not delete Last.fm row.
        var result = await reconcile.ApplyWindowAsync(
            from,
            to,
            new PlayWindowResult(
            [
                new IncomingPlay(ScrobbleProvider.Maloja, "B", "MalojaPlay", null, malojaOnly, null, null, null, false, "{}")
            ],
            1,
            1,
            true,
            null),
            ScrobbleProvider.Maloja,
            allowDeletes: true,
            CancellationToken.None);

        Assert.Equal(0, result.Removed);
        Assert.Equal(2, await harness.Db.Scrobbles.CountAsync());
        Assert.True(await harness.Db.Scrobbles.AnyAsync(s => s.UnixTimestamp == keepLastFm && s.Source == ScrobbleProvider.LastFm));
    }

    [Fact]
    public void IncomingPlay_maps_from_lastfm()
    {
        var mapped = IncomingPlayMapper.FromLastFm(
            new LastFmRecentTrack("A", "B", "C", 10, "t", "a", "l", false, "{}"),
            ScrobbleProvider.LibreFm);
        Assert.Equal(ScrobbleProvider.LibreFm, mapped.Source);
        Assert.Equal("A", mapped.ArtistName);
        Assert.Equal(10, mapped.TimestampUnix);
    }

    [Fact]
    public void Coverage_includes_maloja_row()
    {
        var rows = CatalogCoverage.Build(
            10,
            withMbid: 5,
            withDuration: 7,
            withTags: 3,
            withAudio: 1,
            [new(nameof(EnrichmentSource.Maloja), nameof(SourceFetchStatus.Success), 3)],
            new AggregationOptions { EnableMalojaAlbumArt = true },
            lastFmConfigured: true,
            discogsConfigured: true,
            audioDbConfigured: true,
            malojaConfigured: true);
        var maloja = rows.Single(r => r.Name == "Maloja");
        Assert.True(maloja.Enabled);
        Assert.Equal(3, maloja.Hits);
    }
}
