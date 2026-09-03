using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class CatalogServiceTests
{
    [Fact]
    public async Task AddAlias_collapses_duplicate_names_from_musicbrainz_locales()
    {
        await using var harness = await TestDb.CreateAsync();
        var artist = await SeedArtistAsync(harness.Db, "Ryuichi Sakamoto");
        var catalog = new CatalogService(harness.Db);

        foreach (var locale in new[] { "de", "en", "fr", "nl" })
        {
            await catalog.AddAliasIfMissingAsync(
                artist,
                "Ryūichi Sakamoto",
                "MusicBrainz",
                locale,
                CancellationToken.None);
        }

        await catalog.SaveChangesIgnoringDuplicateAliasesAsync(CancellationToken.None);

        var aliases = await harness.Db.ArtistAliases.ToListAsync();
        Assert.Single(aliases);
        Assert.Equal("Ryūichi Sakamoto", aliases[0].Name);
        Assert.Equal("en", aliases[0].Locale);
    }

    [Fact]
    public async Task AddAlias_skips_the_artist_display_name()
    {
        await using var harness = await TestDb.CreateAsync();
        var artist = await SeedArtistAsync(harness.Db, "NateWantsToBattle");
        var catalog = new CatalogService(harness.Db);

        await catalog.AddAliasIfMissingAsync(
            artist, "NateWantsToBattle", "MusicBrainz", "en", CancellationToken.None);
        await catalog.AddAliasIfMissingAsync(
            artist, "  NateWantsToBattle  ", "MusicBrainz", "en", CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        Assert.Empty(await harness.Db.ArtistAliases.ToListAsync());
    }

    [Fact]
    public async Task AddAlias_is_idempotent_when_the_row_already_exists()
    {
        await using var harness = await TestDb.CreateAsync();
        var artist = await SeedArtistAsync(harness.Db, "ichigo");
        var catalog = new CatalogService(harness.Db);

        await catalog.AddAliasIfMissingAsync(artist, "ichigo from 岸田教団&THE明星ロケッツ", "MusicBrainz", "en", CancellationToken.None);
        await harness.Db.SaveChangesAsync();
        await catalog.AddAliasIfMissingAsync(artist, "ichigo from 岸田教団&THE明星ロケッツ", "MusicBrainz", "ja", CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        Assert.Equal(1, await harness.Db.ArtistAliases.CountAsync());
    }

    [Fact]
    public async Task Duplicate_inserts_throw_the_sqlite_unique_error_from_the_logs()
    {
        await using var harness = await TestDb.CreateAsync();
        var artist = await SeedArtistAsync(harness.Db, "Ryuichi Sakamoto");
        harness.Db.ArtistAliases.AddRange(
            new ArtistAlias { ArtistId = artist.Id, Name = "Ryūichi Sakamoto", Source = "MusicBrainz", Locale = "de" },
            new ArtistAlias { ArtistId = artist.Id, Name = "Ryūichi Sakamoto", Source = "MusicBrainz", Locale = "en" });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => harness.Db.SaveChangesAsync());
        Assert.Contains(
            "UNIQUE constraint failed: ArtistAliases",
            ex.GetBaseException().Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveChangesIgnoringDuplicateAliases_keeps_the_rest_of_the_match()
    {
        await using var harness = await TestDb.CreateAsync();
        var artist = await SeedArtistAsync(harness.Db, "Ryuichi Sakamoto");
        var catalog = new CatalogService(harness.Db);

        await catalog.AddAliasIfMissingAsync(artist, "R.S.", "MusicBrainz", null, CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        artist.SortName = "Sakamoto, Ryūichi";
        harness.Db.ArtistAliases.Add(new ArtistAlias
        {
            ArtistId = artist.Id,
            Name = "R.S.",
            Source = "MusicBrainz",
            Locale = "en"
        });

        await catalog.SaveChangesIgnoringDuplicateAliasesAsync(CancellationToken.None);

        Assert.Equal("Sakamoto, Ryūichi", await harness.Db.Artists.Select(a => a.SortName).SingleAsync());
        Assert.Equal(1, await harness.Db.ArtistAliases.CountAsync());
    }

    [Fact]
    public async Task SaveChangesIgnoringDuplicateAliases_still_keeps_a_new_artist_mbid()
    {
        await using var harness = await TestDb.CreateAsync();
        var artist = await SeedArtistAsync(harness.Db, "Ryuichi Sakamoto");
        var catalog = new CatalogService(harness.Db);

        await catalog.AddAliasIfMissingAsync(artist, "R.S.", "MusicBrainz", null, CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        artist.Mbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        harness.Db.ArtistAliases.Add(new ArtistAlias
        {
            ArtistId = artist.Id,
            Name = "R.S.",
            Source = "MusicBrainz",
            Locale = "en"
        });

        await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(CancellationToken.None);

        Assert.Equal(
            "c053d1ea-d348-43a0-8bb2-658fd8c4810a",
            await harness.Db.Artists.Select(a => a.Mbid).SingleAsync());
        Assert.Equal(1, await harness.Db.ArtistAliases.CountAsync());
    }

    [Fact]
    public async Task Duplicate_artist_mbid_throws_the_sqlite_unique_error_from_the_logs()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string mbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        harness.Db.Artists.AddRange(
            new Artist { Name = "Mori Calliope", Mbid = mbid, CreatedAt = now, UpdatedAt = now },
            new Artist { Name = "Calliope Mori", CreatedAt = now, UpdatedAt = now });
        await harness.Db.SaveChangesAsync();

        var duplicate = await harness.Db.Artists.SingleAsync(a => a.Name == "Calliope Mori");
        duplicate.Mbid = mbid;

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => harness.Db.SaveChangesAsync());
        Assert.Contains(
            "UNIQUE constraint failed: Artists.Mbid",
            ex.GetBaseException().Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryCoalesceArtistMbid_skips_when_another_artist_already_has_it()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string mbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        var keep = new Artist { Name = "Mori Calliope", Mbid = mbid, CreatedAt = now, UpdatedAt = now };
        var other = new Artist { Name = "Calliope Mori", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.AddRange(keep, other);
        await harness.Db.SaveChangesAsync();

        var catalog = new CatalogService(harness.Db);
        await catalog.TryCoalesceArtistMbidAsync(other, mbid, CancellationToken.None);
        other.LastFmUrl = "https://www.last.fm/music/Calliope+Mori";
        await harness.Db.SaveChangesAsync();

        Assert.Equal(mbid, await harness.Db.Artists.Where(a => a.Id == keep.Id).Select(a => a.Mbid).SingleAsync());
        Assert.Null(await harness.Db.Artists.Where(a => a.Id == other.Id).Select(a => a.Mbid).SingleAsync());
        Assert.Equal(
            "https://www.last.fm/music/Calliope+Mori",
            await harness.Db.Artists.Where(a => a.Id == other.Id).Select(a => a.LastFmUrl).SingleAsync());
    }

    [Fact]
    public async Task GetOrCreateArtist_does_not_copy_a_taken_mbid_onto_a_name_match()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string mbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        harness.Db.Artists.AddRange(
            new Artist { Name = "Mori Calliope", Mbid = mbid, CreatedAt = now, UpdatedAt = now },
            new Artist { Name = "Calliope Mori", CreatedAt = now, UpdatedAt = now });
        await harness.Db.SaveChangesAsync();

        var catalog = new CatalogService(harness.Db);
        var byName = await catalog.GetOrCreateArtistAsync("Calliope Mori", mbid, CancellationToken.None);
        await harness.Db.SaveChangesAsync();

        Assert.Equal("Mori Calliope", byName.Name);
        Assert.Equal(mbid, byName.Mbid);
        Assert.Null(await harness.Db.Artists.Where(a => a.Name == "Calliope Mori").Select(a => a.Mbid).SingleAsync());
    }

    [Fact]
    public async Task SaveChangesIgnoringDuplicateCatalogKeys_reverts_artist_mbid_and_keeps_other_fields()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string mbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        var keep = new Artist { Name = "Mori Calliope", Mbid = mbid, CreatedAt = now, UpdatedAt = now };
        var other = new Artist { Name = "Calliope Mori", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.AddRange(keep, other);
        await harness.Db.SaveChangesAsync();

        other.Mbid = mbid;
        other.LastFmUrl = "https://www.last.fm/music/Calliope+Mori";
        var catalog = new CatalogService(harness.Db);
        await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(CancellationToken.None);

        Assert.Equal(mbid, await harness.Db.Artists.Where(a => a.Id == keep.Id).Select(a => a.Mbid).SingleAsync());
        Assert.Null(await harness.Db.Artists.Where(a => a.Id == other.Id).Select(a => a.Mbid).SingleAsync());
        Assert.Equal(
            "https://www.last.fm/music/Calliope+Mori",
            await harness.Db.Artists.Where(a => a.Id == other.Id).Select(a => a.LastFmUrl).SingleAsync());
    }

    [Fact]
    public async Task RevertUnsavedMbidAssignments_lets_the_enrichment_error_row_save()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string mbid = "c053d1ea-d348-43a0-8bb2-658fd8c4810a";
        var keep = new Artist { Name = "Mori Calliope", Mbid = mbid, CreatedAt = now, UpdatedAt = now };
        var other = new Artist { Name = "Calliope Mori", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.AddRange(keep, other);
        await harness.Db.SaveChangesAsync();

        var track = new Track
        {
            ArtistId = other.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-error-save",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.Add(track);
        await harness.Db.SaveChangesAsync();

        var payload = new TrackSourcePayload
        {
            TrackId = track.Id,
            Source = EnrichmentSource.LastFm,
            Status = SourceFetchStatus.NotStarted
        };
        harness.Db.TrackSourcePayloads.Add(payload);
        await harness.Db.SaveChangesAsync();

        other.Mbid = mbid;
        payload.Status = SourceFetchStatus.Error;
        payload.ErrorMessage = "UNIQUE constraint failed: Artists.Mbid";
        payload.FetchedAt = DateTimeOffset.UtcNow;

        var catalog = new CatalogService(harness.Db);
        await Assert.ThrowsAsync<DbUpdateException>(() => harness.Db.SaveChangesAsync());

        catalog.RevertUnsavedMbidAssignments();
        await harness.Db.SaveChangesAsync();

        Assert.Null(await harness.Db.Artists.Where(a => a.Id == other.Id).Select(a => a.Mbid).SingleAsync());
        Assert.Equal(
            SourceFetchStatus.Error,
            await harness.Db.TrackSourcePayloads.Select(p => p.Status).SingleAsync());
    }

    [Fact]
    public async Task TryCoalesceTrackMbid_skips_when_another_track_already_has_it()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        const string mbid = "81730195-b0f4-45e6-9974-5f198b356194";
        var artist = new Artist { Name = "Mori Calliope", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();
        var keep = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose-Lose Days",
            Fingerprint = "fp-keep",
            Mbid = mbid,
            CreatedAt = now,
            UpdatedAt = now
        };
        var other = new Track
        {
            ArtistId = artist.Id,
            Title = "Lose Lose Days",
            Fingerprint = "fp-other",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.AddRange(keep, other);
        await harness.Db.SaveChangesAsync();

        var catalog = new CatalogService(harness.Db);
        await catalog.TryCoalesceTrackMbidAsync(other, mbid, CancellationToken.None);
        other.Summary = "kept";
        await harness.Db.SaveChangesAsync();

        Assert.Equal(mbid, await harness.Db.Tracks.Where(t => t.Id == keep.Id).Select(t => t.Mbid).SingleAsync());
        Assert.Null(await harness.Db.Tracks.Where(t => t.Id == other.Id).Select(t => t.Mbid).SingleAsync());
        Assert.Equal("kept", await harness.Db.Tracks.Where(t => t.Id == other.Id).Select(t => t.Summary).SingleAsync());
    }

    private static async Task<Artist> SeedArtistAsync(AppDbContext db, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = name, SortName = name, CreatedAt = now, UpdatedAt = now };
        db.Artists.Add(artist);
        await db.SaveChangesAsync();
        return artist;
    }
}
