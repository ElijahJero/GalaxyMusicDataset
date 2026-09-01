using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.MusicBrainz;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class TagServiceTests
{
    [Fact]
    public async Task ApplyTags_collapses_musicbrainz_genre_and_tag_overlap()
    {
        await using var harness = await TestDb.CreateAsync();
        var track = await SeedTrackAsync(harness.Db);
        var json = """
            {
              "id": "rec-1",
              "title": "Lilac",
              "genres": [{ "name": "j-pop", "count": 2 }, { "name": "pop", "count": 1 }],
              "tags": [
                { "name": "j-pop", "count": 4 },
                { "name": "J-Pop", "count": 1 },
                { "name": "anime", "count": 3 }
              ]
            }
            """;
        var details = MusicBrainzClient.ParseRecordingDetails(json);
        Assert.NotNull(details);

        var tagPairs = details!.Genres.Select(g => (g, 80))
            .Concat(details.Tags.Select(t => (t.Name, Math.Max(1, t.Count))));
        var tags = new TagService(harness.Db);
        await tags.ApplyTagsAsync(track.Id, EnrichmentSource.MusicBrainz, tagPairs, CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        var links = await harness.Db.TrackTags.Include(t => t.Tag)
            .Where(t => t.TrackId == track.Id)
            .OrderBy(t => t.Tag.NormalizedName)
            .ToListAsync();
        Assert.Equal(3, links.Count);
        Assert.Equal(["anime", "j-pop", "pop"], links.Select(l => l.Tag.NormalizedName).ToArray());
        Assert.Equal(80, links.Single(l => l.Tag.NormalizedName == "j-pop").Weight);
        Assert.Equal(80, links.Single(l => l.Tag.NormalizedName == "pop").Weight);
        Assert.Equal(3, links.Single(l => l.Tag.NormalizedName == "anime").Weight);
    }

    [Fact]
    public async Task ApplyTags_is_idempotent_across_saves()
    {
        await using var harness = await TestDb.CreateAsync();
        var track = await SeedTrackAsync(harness.Db);
        var tags = new TagService(harness.Db);
        var pairs = new (string, int)[] { ("hip hop", 80), ("hip hop", 14), ("rap", 10) };

        await tags.ApplyTagsAsync(track.Id, EnrichmentSource.MusicBrainz, pairs, CancellationToken.None);
        await harness.Db.SaveChangesAsync();
        await tags.ApplyTagsAsync(track.Id, EnrichmentSource.MusicBrainz, [("hip hop", 90), ("rap", 10)], CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        var links = await harness.Db.TrackTags.Include(t => t.Tag)
            .Where(t => t.TrackId == track.Id)
            .ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal(90, links.Single(l => l.Tag.NormalizedName == "hip hop").Weight);
        Assert.Equal(1, await harness.Db.Tags.CountAsync(t => t.NormalizedName == "hip hop"));
    }

    [Fact]
    public async Task ApplyTags_sees_unsaved_links_in_the_change_tracker()
    {
        await using var harness = await TestDb.CreateAsync();
        var track = await SeedTrackAsync(harness.Db);
        var tags = new TagService(harness.Db);

        await tags.ApplyTagsAsync(track.Id, EnrichmentSource.Discogs, [("Pop", 50)], CancellationToken.None);
        await tags.ApplyTagsAsync(track.Id, EnrichmentSource.Discogs, [("pop", 40), ("Anison", 40)], CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        var links = await harness.Db.TrackTags.Include(t => t.Tag)
            .Where(t => t.TrackId == track.Id && t.Source == EnrichmentSource.Discogs)
            .ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal(40, links.Single(l => l.Tag.NormalizedName == "pop").Weight);
        Assert.Equal(40, links.Single(l => l.Tag.NormalizedName == "anison").Weight);
    }

    [Fact]
    public async Task ApplyTags_reuses_an_existing_normalized_name()
    {
        await using var harness = await TestDb.CreateAsync();
        var track = await SeedTrackAsync(harness.Db);
        harness.Db.Tags.Add(new Tag { Name = "J-Pop", NormalizedName = "j-pop" });
        await harness.Db.SaveChangesAsync();

        var tags = new TagService(harness.Db);
        await tags.ApplyTagsAsync(track.Id, EnrichmentSource.LastFm, [("j-pop", 12), ("J-Pop", 4)], CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        Assert.Equal(1, await harness.Db.Tags.CountAsync());
        Assert.Equal(1, await harness.Db.TrackTags.CountAsync());
        Assert.Equal(12, await harness.Db.TrackTags.Select(t => t.Weight).SingleAsync());
    }

    private static async Task<Track> SeedTrackAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Kyasu", CreatedAt = now, UpdatedAt = now };
        db.Artists.Add(artist);
        await db.SaveChangesAsync();
        var track = new Track
        {
            ArtistId = artist.Id,
            Title = "Lilac",
            Fingerprint = "fp-lilac",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track;
    }
}
