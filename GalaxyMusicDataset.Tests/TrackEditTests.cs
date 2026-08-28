using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class TrackEditTests
{
    [Fact]
    public async Task Edit_updates_names_and_fingerprint()
    {
        await using var harness = await TestDb.CreateAsync();
        var catalog = new CatalogService(harness.Db);
        var artist = await catalog.GetOrCreateArtistAsync("Old Artist", null, CancellationToken.None);
        var track = new Track
        {
            ArtistId = artist.Id,
            Title = "Old Title",
            Fingerprint = TrackFingerprint.Compute("Old Artist", "Old Title"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        harness.Db.Tracks.Add(track);
        await harness.Db.SaveChangesAsync();
        harness.Db.TrackLookups.Add(new TrackLookup
        {
            Fingerprint = track.Fingerprint,
            TrackId = track.Id,
            ArtistName = "Old Artist",
            TrackName = "Old Title",
            Status = LookupStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var editor = new TrackEditService(harness.Db, catalog);
        var result = await editor.SaveAsync(new TrackEditInput
        {
            Id = track.Id,
            ArtistName = "New Artist",
            Title = "New Title",
            DurationSeconds = 180
        }, CancellationToken.None);

        Assert.Equal(track.Id, result.TrackId);
        await harness.Db.Entry(track).ReloadAsync();
        Assert.Equal("New Title", track.Title);
        Assert.Equal(180000, track.DurationMs);
        Assert.Equal(TrackFingerprint.Compute("New Artist", "New Title"), track.Fingerprint);
        var lookup = await harness.Db.TrackLookups.SingleAsync();
        Assert.Equal(track.Fingerprint, lookup.Fingerprint);
        Assert.Equal("New Artist", lookup.ArtistName);
    }

    [Fact]
    public async Task Edit_merges_when_fingerprint_collides()
    {
        await using var harness = await TestDb.CreateAsync();
        var catalog = new CatalogService(harness.Db);
        var artist = await catalog.GetOrCreateArtistAsync("A", null, CancellationToken.None);
        var keep = new Track
        {
            ArtistId = artist.Id,
            Title = "Same",
            Fingerprint = TrackFingerprint.Compute("A", "Same"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var drop = new Track
        {
            ArtistId = artist.Id,
            Title = "Other",
            Fingerprint = TrackFingerprint.Compute("A", "Other"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        harness.Db.Tracks.AddRange(keep, drop);
        await harness.Db.SaveChangesAsync();
        harness.Db.Scrobbles.AddRange(
            new Scrobble
            {
                TrackId = keep.Id,
                PlayedAt = DateTimeOffset.FromUnixTimeSeconds(100),
                UnixTimestamp = 100,
                OriginalArtist = "A",
                OriginalTitle = "Same"
            },
            new Scrobble
            {
                TrackId = drop.Id,
                PlayedAt = DateTimeOffset.FromUnixTimeSeconds(200),
                UnixTimestamp = 200,
                OriginalArtist = "A",
                OriginalTitle = "Other"
            });
        await harness.Db.SaveChangesAsync();

        var editor = new TrackEditService(harness.Db, catalog);
        var result = await editor.SaveAsync(new TrackEditInput
        {
            Id = drop.Id,
            ArtistName = "A",
            Title = "Same"
        }, CancellationToken.None);

        Assert.True(result.Merged);
        Assert.Equal(1, await harness.Db.Tracks.CountAsync());
        Assert.Equal(2, await harness.Db.Scrobbles.CountAsync(s => s.TrackId == result.TrackId));
    }
}
