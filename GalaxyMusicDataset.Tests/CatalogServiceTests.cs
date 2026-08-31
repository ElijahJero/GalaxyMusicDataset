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

    private static async Task<Artist> SeedArtistAsync(AppDbContext db, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = name, SortName = name, CreatedAt = now, UpdatedAt = now };
        db.Artists.Add(artist);
        await db.SaveChangesAsync();
        return artist;
    }
}
