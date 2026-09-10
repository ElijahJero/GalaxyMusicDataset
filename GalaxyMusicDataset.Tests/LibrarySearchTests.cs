using GalaxyMusicDataset.Data;
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
    public void Search_matches_hyphen_underscore_and_concatenated_names()
    {
        using var engine = new LibrarySearchEngine();
        engine.Rebuild(
        [
            new(1, "Song", "kathy-chan", null, []),
            new(2, "Other", "kathy_chan", null, []),
            new(3, "Plain", "someone else", null, [])
        ]);

        Assert.Equal([1, 2], Ids(engine.Search("kathychan", null, null, null)));
        Assert.Equal([1, 2], Ids(engine.Search("kathy-chan", null, null, null)));
        Assert.Equal([1, 2], Ids(engine.Search("kathy_chan", null, null, null)));
        Assert.DoesNotContain(3L, engine.Search("kathychan", null, null, null));
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

    [Fact]
    public async Task Library_list_omits_payload_json_and_audio_raw()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var artist = new Artist { Name = "kathy-chan", CreatedAt = now, UpdatedAt = now };
        harness.Db.Artists.Add(artist);
        await harness.Db.SaveChangesAsync();
        var track = new Track
        {
            ArtistId = artist.Id,
            Title = "Demo",
            Fingerprint = "fp-demo",
            CreatedAt = now,
            UpdatedAt = now
        };
        harness.Db.Tracks.Add(track);
        await harness.Db.SaveChangesAsync();

        var blob = new string('x', 8000);
        harness.Db.TrackSourcePayloads.Add(new TrackSourcePayload
        {
            TrackId = track.Id,
            Source = EnrichmentSource.MusicBrainz,
            Status = SourceFetchStatus.Success,
            ExternalId = "mbid",
            PayloadJson = blob
        });
        harness.Db.TrackAudioProfiles.Add(new TrackAudioProfile
        {
            TrackId = track.Id,
            AnalyzedAt = now,
            Bpm = 128,
            RawJson = blob
        });
        await harness.Db.SaveChangesAsync();
        harness.Db.TrackAudioLabels.Add(new TrackAudioLabel
        {
            TrackId = track.Id,
            Kind = AudioLabelKind.Genre,
            Name = "Electronic/House",
            Score = 0.9
        });
        var tag = new Tag { Name = "j-pop", NormalizedName = "j-pop" };
        harness.Db.Tags.Add(tag);
        await harness.Db.SaveChangesAsync();
        harness.Db.TrackTags.Add(new TrackTag
        {
            TrackId = track.Id,
            TagId = tag.Id,
            Source = EnrichmentSource.LastFm,
            Weight = 100
        });
        await harness.Db.SaveChangesAsync();

        var query = LibraryListQuery.ApplySort(harness.Db.Tracks.AsNoTracking(), "title");
        var page = await LibraryListQuery.LoadPageAsync(harness.Db, query, 1, 50, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("Demo", item.Track.Title);
        Assert.Contains("j-pop", item.Track.Tags.Select(t => t.Tag.Name));
        Assert.NotNull(item.Audio);
        Assert.Null(item.Audio.RawJson);
        Assert.Equal(128, item.Audio.Bpm);
        Assert.Contains(item.Audio.Genres, g => g.Name == "Electronic/House");
        var source = Assert.Single(item.Sources);
        Assert.Equal("MusicBrainz", source.Source);
        Assert.Null(source.Json);
        Assert.Empty(item.Track.SourcePayloads);
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
