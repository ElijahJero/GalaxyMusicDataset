using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.LastFm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Tests;

public class LastFmEnrichmentTests
{
    [Fact]
    public async Task ApplyLastFm_keeps_lastfm_data_when_artist_mbid_is_already_taken()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string artistMbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        var keep = new Artist { Name = "Mori Calliope", Mbid = artistMbid, CreatedAt = now, UpdatedAt = now };
        var other = new Artist { Name = "Calliope Mori", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.AddRange(keep, other);
        await harness.Db.SaveChangesAsync();

        var track = new Track
        {
            ArtistId = other.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-lastfm-mbid",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.Add(track);
        await harness.Db.SaveChangesAsync();
        await harness.Db.Entry(track).Reference(t => t.Artist).LoadAsync();

        var payload = new TrackSourcePayload
        {
            TrackId = track.Id,
            Source = EnrichmentSource.LastFm,
            Status = SourceFetchStatus.NotStarted
        };
        harness.Db.TrackSourcePayloads.Add(payload);
        await harness.Db.SaveChangesAsync();

        var catalog = new CatalogService(harness.Db);
        var service = new MetadataEnrichmentService(
            harness.Db,
            null!,
            new TagService(harness.Db),
            catalog,
            new AggregationProgress(),
            new StaticMonitor<AggregationOptions>(new AggregationOptions { EnableMusicBrainz = false }));

        var info = new LastFmTrackInfo(
            "81730195-b0f4-45e6-9974-5f198b356194",
            210000,
            "UnAlive",
            "album-mbid",
            artistMbid,
            "https://www.last.fm/music/Mori+Calliope",
            "https://www.last.fm/music/Mori+Calliope/_/Lose-Lose+Days",
            "A song.",
            "https://example.com/cover.jpg",
            [new LastFmTag("j-pop", 12)],
            "{}");

        payload.Status = SourceFetchStatus.Success;
        payload.PayloadJson = info.RawJson;
        payload.ExternalId = info.Mbid;
        payload.FetchedAt = DateTimeOffset.UtcNow;
        await service.ApplyLastFmAsync(track, info, CancellationToken.None);
        await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(CancellationToken.None);

        var saved = await harness.Db.Tracks.Include(t => t.Artist).SingleAsync(t => t.Id == track.Id);
        Assert.Equal(210000, saved.DurationMs);
        Assert.Equal("81730195-b0f4-45e6-9974-5f198b356194", saved.Mbid);
        Assert.Equal("A song.", saved.Summary);
        Assert.Equal("https://www.last.fm/music/Mori+Calliope", saved.Artist.LastFmUrl);
        Assert.Null(saved.Artist.Mbid);
        Assert.Equal(artistMbid, await harness.Db.Artists.Where(a => a.Id == keep.Id).Select(a => a.Mbid).SingleAsync());
        Assert.Equal(SourceFetchStatus.Success, await harness.Db.TrackSourcePayloads.Select(p => p.Status).SingleAsync());
        Assert.Equal(1, await harness.Db.TrackTags.CountAsync(t => t.TrackId == track.Id));
        Assert.NotNull(saved.AlbumId);
    }

    [Fact]
    public async Task ApplyLastFm_skips_a_track_mbid_another_row_already_owns()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Mori Calliope", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();

        const string trackMbid = "81730195-b0f4-45e6-9974-5f198b356194";
        var keep = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-keep-track",
            Mbid = trackMbid,
            CreatedAt = now,
            UpdatedAt = now
        };
        var other = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose Lose Days",
            Fingerprint = "fp-other-track",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.AddRange(keep, other);
        await harness.Db.SaveChangesAsync();
        await harness.Db.Entry(other).Reference(t => t.Artist).LoadAsync();

        var catalog = new CatalogService(harness.Db);
        var service = new MetadataEnrichmentService(
            harness.Db,
            null!,
            new TagService(harness.Db),
            catalog,
            new AggregationProgress(),
            new StaticMonitor<AggregationOptions>(new AggregationOptions()));

        await service.ApplyLastFmAsync(
            other,
            new LastFmTrackInfo(
                trackMbid,
                180000,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                "{}"),
            CancellationToken.None);
        await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(CancellationToken.None);

        Assert.Null(await harness.Db.Tracks.Where(t => t.Id == other.Id).Select(t => t.Mbid).SingleAsync());
        Assert.Equal(trackMbid, await harness.Db.Tracks.Where(t => t.Id == keep.Id).Select(t => t.Mbid).SingleAsync());
        Assert.Equal(180000, await harness.Db.Tracks.Where(t => t.Id == other.Id).Select(t => t.DurationMs).SingleAsync());
    }

    [Fact]
    public async Task Next_lastfm_track_is_still_pending_after_a_sibling_succeeds()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Kyasu", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();

        var first = new Track
        {
            ArtistId = artist.Id,
            Title = "Lilac",
            Fingerprint = "fp-lilac",
            CreatedAt = now,
            UpdatedAt = now
        };
        var second = new Track
        {
            ArtistId = artist.Id,
            Title = "Other",
            Fingerprint = "fp-other",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.AddRange(first, second);
        await harness.Db.SaveChangesAsync();

        harness.Db.TrackSourcePayloads.Add(new TrackSourcePayload
        {
            TrackId = first.Id,
            Source = EnrichmentSource.LastFm,
            Status = SourceFetchStatus.Success,
            FetchedAt = now
        });
        await harness.Db.SaveChangesAsync();

        var stillPending = await harness.Db.Tracks
            .Where(t => !t.SourcePayloads.Any(p =>
                p.Source == EnrichmentSource.LastFm &&
                (p.Status == SourceFetchStatus.Success ||
                 p.Status == SourceFetchStatus.NotFound ||
                 p.Status == SourceFetchStatus.Skipped)))
            .OrderBy(t => t.Id)
            .Select(t => t.Title)
            .ToListAsync();

        Assert.Equal(["Other"], stillPending);
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
