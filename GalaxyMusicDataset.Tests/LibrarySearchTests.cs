using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Pages;
using GalaxyMusicDataset.Services.Search;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class LibrarySearchTests
{
    [Fact]
    public void Search_is_case_insensitive_and_matches_partial_tokens()
    {
        using var engine = new LibrarySearchEngine();
        engine.Rebuild(SampleCatalog());

        Assert.Equal([1], Ids(engine.Search("mori", null, null, null)));
        Assert.Equal([1], Ids(engine.Search("CALLI", null, null, null)));
        Assert.Equal([2], Ids(engine.Search("loading", null, null, null)));
        Assert.DoesNotContain(5L, engine.Search("calli", null, null, null));
        Assert.Equal([3], Ids(engine.Search("stay house", null, null, null)));
    }

    [Fact]
    public void Search_tolerates_small_typos_and_field_filters()
    {
        using var engine = new LibrarySearchEngine();
        engine.Rebuild(SampleCatalog());

        Assert.Equal([1], Ids(engine.Search("caliope", null, null, null)));
        Assert.Equal([2, 4], Ids(engine.Search(null, "four", null, null)));
        Assert.Equal([3], Ids(engine.Search(null, null, "really want", null)));
        Assert.Equal([1], Ids(engine.Search(null, null, null, "disaster")));
        Assert.Empty(engine.Search(null, "mori", "loading", null));
    }

    [Fact]
    public void Search_matches_aliases_and_kana_via_romaji()
    {
        using var engine = new LibrarySearchEngine();
        engine.Rebuild(SampleCatalog());

        Assert.Equal([1], Ids(engine.Search("calli", null, null, null)));
        Assert.Equal([4], Ids(engine.Search("susume", null, null, null)));
        Assert.Equal([4], Ids(engine.Search(null, null, "ススメ", null)));
    }

    [Fact]
    public async Task Library_filters_use_lucene_ids_with_other_predicates()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "Mori Calliope", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();
        harness.Db.Tracks.AddRange(
            new Track
            {
                ArtistId = artist.Id,
                Title = "Lose-Lose Days",
                Fingerprint = "fp-lose",
                Mbid = "mbid-1",
                CreatedAt = now,
                UpdatedAt = now
            },
            new Track
            {
                ArtistId = artist.Id,
                Title = "INSOMNIAC BLACK",
                Fingerprint = "fp-insom",
                CreatedAt = now,
                UpdatedAt = now
            });
        await harness.Db.SaveChangesAsync();

        using var search = new LibrarySearchService();
        await search.EnsureCurrentAsync(harness.Db, CancellationToken.None);

        var caseInsensitive = await LibraryFilters.Apply(
                harness.Db.Tracks, harness.Db, search, "lose days", null, null, null, null, null, null, null)
            .Select(t => t.Title)
            .ToListAsync();
        Assert.Equal(["Lose-Lose Days"], caseInsensitive);

        var missingMbid = await LibraryFilters.Apply(
                harness.Db.Tracks, harness.Db, search, "mori", null, null, null, "no", null, null, null)
            .Select(t => t.Title)
            .ToListAsync();
        Assert.Equal(["INSOMNIAC BLACK"], missingMbid);
    }

    private static long[] Ids(IReadOnlyList<long> ids) => [.. ids.OrderBy(id => id)];

    private static List<LibrarySearchDocument> SampleCatalog() =>
    [
        new(1, "INSOMNIAC BLACK", "Mori Calliope", "DISASTERPIECE", ["Calliope Mori"]),
        new(2, "Now Loading!!!!", "fourfolium", "JUMPin' JUMP UP!!!!", []),
        new(3, "I Really Want to Stay at Your House", "Samuel Kim", "I Really Want to Stay at Your House", []),
        new(4, "ススメRunner!!(instrumental)", "fourfolium", "TVアニメ「NEW GAME!!」オープニングテーマ", []),
        new(5, "Shopping Malls", "nihmune", "Neutral Front", [])
    ];
}
