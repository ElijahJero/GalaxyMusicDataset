using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.MusicBrainz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Tests;

public class MusicBrainzAcceptTests
{
    [Fact]
    public async Task Accept_sets_recording_and_artist_mbids()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Mori Calliope", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();

        var track = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-accept",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.Add(track);
        await harness.Db.SaveChangesAsync();

        var recordingMbid = "81730195-b0f4-45e6-9974-5f198b356194";
        var artistMbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        var lookup = new TrackLookup
        {
            Fingerprint = "fp-accept",
            TrackId = track.Id,
            ArtistName = "Mori Calliope",
            TrackName = "Lose-Lose Days",
            AlbumName = "UnAlive",
            Status = LookupStatus.NeedsReview,
            CandidateJson = JsonSerializer.Serialize(new[]
            {
                new RecordingCandidate(
                    recordingMbid,
                    "Lose-Lose Days",
                    "Mori Calliope",
                    "UnAlive",
                    210000,
                    artistMbid,
                    "release-mbid",
                    null,
                    0.81)
            }),
            CreatedAt = now
        };
        harness.Db.TrackLookups.Add(lookup);
        await harness.Db.SaveChangesAsync();

        var service = CreateService(harness.Db);
        await service.AcceptCandidateAsync(lookup.Id, recordingMbid, CancellationToken.None);

        var accepted = await harness.Db.TrackLookups.SingleAsync(l => l.Id == lookup.Id);
        Assert.Equal(LookupStatus.ManualMatched, accepted.Status);
        Assert.Equal(recordingMbid, accepted.MatchedMbid);
        Assert.Equal(track.Id, accepted.TrackId);

        var matched = await harness.Db.Tracks.Include(t => t.Artist).SingleAsync(t => t.Id == track.Id);
        Assert.Equal(recordingMbid, matched.Mbid);
        Assert.Equal(210000, matched.DurationMs);
        Assert.Equal(artistMbid, matched.Artist.Mbid);
        Assert.NotNull(matched.AlbumId);
    }

    [Fact]
    public async Task Accept_merges_when_another_track_already_has_the_mbid()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Mori Calliope", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();

        var recordingMbid = "81730195-b0f4-45e6-9974-5f198b356194";
        var artistMbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";

        var keep = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-keep",
            Mbid = recordingMbid,
            CreatedAt = now,
            UpdatedAt = now
        };
        var drop = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose Lose Days",
            Fingerprint = "fp-drop",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.AddRange(keep, drop);
        await harness.Db.SaveChangesAsync();

        harness.Db.Scrobbles.Add(new Scrobble
        {
            TrackId = drop.Id,
            PlayedAt = now,
            UnixTimestamp = 1_700_000_001,
            OriginalArtist = "Mori Calliope",
            OriginalTitle = "Lose Lose Days"
        });

        var lookup = new TrackLookup
        {
            Fingerprint = "fp-drop",
            TrackId = drop.Id,
            ArtistName = "Mori Calliope",
            TrackName = "Lose Lose Days",
            AlbumName = "UnAlive",
            Status = LookupStatus.NeedsReview,
            CandidateJson = JsonSerializer.Serialize(new[]
            {
                new RecordingCandidate(
                    recordingMbid,
                    "Lose-Lose Days",
                    "Mori Calliope",
                    "UnAlive",
                    210000,
                    artistMbid,
                    "release-mbid",
                    null,
                    0.81)
            }),
            CreatedAt = now
        };
        harness.Db.TrackLookups.Add(lookup);
        await harness.Db.SaveChangesAsync();

        var service = CreateService(harness.Db);
        await service.AcceptCandidateAsync(lookup.Id, recordingMbid, CancellationToken.None);

        Assert.False(await harness.Db.Tracks.AnyAsync(t => t.Id == drop.Id));
        var surviving = await harness.Db.Tracks.Include(t => t.Artist).SingleAsync(t => t.Mbid == recordingMbid);
        Assert.Equal(keep.Id, surviving.Id);
        Assert.Equal(artistMbid, surviving.Artist.Mbid);
        Assert.Equal(210000, surviving.DurationMs);

        var accepted = await harness.Db.TrackLookups.SingleAsync(l => l.Id == lookup.Id);
        Assert.Equal(LookupStatus.ManualMatched, accepted.Status);
        Assert.Equal(keep.Id, accepted.TrackId);
        Assert.Equal(keep.Id, await harness.Db.Scrobbles.Select(s => s.TrackId).SingleAsync());
    }

    [Fact]
    public async Task Accept_does_not_overwrite_an_existing_artist_mbid()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var keepArtist = new Artist
        {
            Name = "Mori Calliope",
            Mbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a",
            CreatedAt = now,
            UpdatedAt = now
        };
        var otherArtist = new Artist { Name = "Calliope Mori", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.AddRange(keepArtist, otherArtist);
        await harness.Db.SaveChangesAsync();

        var recordingMbid = "81730195-b0f4-45e6-9974-5f198b356194";
        var keep = new Track
        {
            ArtistId = keepArtist.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-keep-artist",
            Mbid = recordingMbid,
            CreatedAt = now,
            UpdatedAt = now
        };
        var drop = new Track
        {
            ArtistId = otherArtist.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-drop-artist",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.AddRange(keep, drop);
        await harness.Db.SaveChangesAsync();

        var lookup = new TrackLookup
        {
            Fingerprint = "fp-drop-artist",
            TrackId = drop.Id,
            ArtistName = "Calliope Mori",
            TrackName = "Lose-Lose Days",
            Status = LookupStatus.NeedsReview,
            CandidateJson = JsonSerializer.Serialize(new[]
            {
                new RecordingCandidate(
                    recordingMbid,
                    "Lose-Lose Days",
                    "Mori Calliope",
                    null,
                    null,
                    keepArtist.Mbid,
                    null,
                    null,
                    0.8)
            }),
            CreatedAt = now
        };
        harness.Db.TrackLookups.Add(lookup);
        await harness.Db.SaveChangesAsync();

        var service = CreateService(harness.Db);
        await service.AcceptCandidateAsync(lookup.Id, recordingMbid, CancellationToken.None);

        var surviving = await harness.Db.Tracks.Include(t => t.Artist).SingleAsync(t => t.Id == keep.Id);
        Assert.Equal(keepArtist.Mbid, surviving.Artist.Mbid);
        Assert.Null(await harness.Db.Artists.Where(a => a.Id == otherArtist.Id).Select(a => a.Mbid).SingleAsync());
        Assert.Equal(keep.Id, await harness.Db.TrackLookups.Select(l => l.TrackId).SingleAsync());
    }

    private static MusicBrainzLookupService CreateService(AppDbContext db) =>
        new(
            db,
            new CatalogService(db),
            null!,
            new AggregationProgress(),
            new StaticMonitor<AggregationOptions>(new AggregationOptions()));
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
