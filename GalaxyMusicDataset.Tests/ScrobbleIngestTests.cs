using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class ScrobbleIngestTests
{
    [Fact]
    public async Task Ingest_is_idempotent_and_dedupes_tracks()
    {
        await using var harness = await TestDb.CreateAsync();
        var catalog = new CatalogService(harness.Db);
        var ingest = new ScrobbleIngestService(harness.Db, catalog);

        var sample = SampleDataSeeder.DefaultSample();
        var first = await ingest.IngestAsync(sample, CancellationToken.None);
        var second = await ingest.IngestAsync(sample, CancellationToken.None);

        Assert.Equal(14, first.Inserted);
        Assert.Equal(0, first.Duplicates);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(14, second.Duplicates);

        Assert.Equal(14, await harness.Db.Scrobbles.CountAsync());
        Assert.Equal(13, await harness.Db.Tracks.CountAsync());
        Assert.Equal(2, await harness.Db.Scrobbles.CountAsync(s => s.Track.Title == "Lose-Lose Days"));

        var lose = await harness.Db.Tracks.SingleAsync(t => t.Title == "Lose-Lose Days");
        Assert.Equal("81730195-b0f4-45e6-9974-5f198b356194", lose.Mbid);

        var akashi = await harness.Db.TrackLookups.SingleAsync(l => l.TrackName == "Akashi Myu");
        Assert.Equal(LookupStatus.Pending, akashi.Status);

        var fingerprint = TrackFingerprint.Compute("Mori Calliope", "Lose-Lose Days");
        Assert.Equal(1, await harness.Db.TrackLookups.CountAsync(l => l.Fingerprint == fingerprint));
        var lookup = await harness.Db.TrackLookups.SingleAsync(l => l.Fingerprint == fingerprint);
        Assert.Equal(LookupStatus.AutoMatched, lookup.Status);
    }

    [Fact]
    public async Task Skips_now_playing()
    {
        await using var harness = await TestDb.CreateAsync();
        var ingest = new ScrobbleIngestService(harness.Db, new CatalogService(harness.Db));
        var result = await ingest.IngestAsync(
        [
            new LastFmRecentTrack("A", "B", null, null, null, null, null, true, "{}")
        ], CancellationToken.None);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, await harness.Db.Scrobbles.CountAsync());
    }

    [Fact]
    public async Task Fingerprint_match_does_not_copy_a_taken_track_mbid()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string mbid = "81730195-b0f4-45e6-9974-5f198b356194";
        var artist = new Artist { Name = "Mori Calliope", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();

        var owner = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose-Lose Days",
            Fingerprint = TrackFingerprint.Compute("Mori Calliope", "Lose-Lose Days"),
            Mbid = mbid,
            CreatedAt = now,
            UpdatedAt = now
        };
        var variant = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose Lose Days",
            Fingerprint = TrackFingerprint.Compute("Calliope Mori", "Lose Lose Days"),
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.AddRange(owner, variant);
        await harness.Db.SaveChangesAsync();

        var ingest = new ScrobbleIngestService(harness.Db, new CatalogService(harness.Db));
        var result = await ingest.IngestAsync(
        [
            new LastFmRecentTrack(
                "Calliope Mori",
                "Lose Lose Days",
                "UnAlive",
                1_700_000_123,
                mbid,
                null,
                null,
                false,
                "{}")
        ], CancellationToken.None);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, await harness.Db.Tracks.CountAsync(t => t.Mbid == mbid));
        Assert.Equal(
            mbid,
            await harness.Db.Tracks.Where(t => t.Id == owner.Id).Select(t => t.Mbid).SingleAsync());
        Assert.Equal(owner.Id, await harness.Db.Scrobbles.Select(s => s.TrackId).SingleAsync());
        Assert.Null(await harness.Db.Tracks.Where(t => t.Id == variant.Id).Select(t => t.Mbid).SingleAsync());
    }
}

internal sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Db { get; }

    private TestDb(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public static async Task<TestDb> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new TestDb(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
