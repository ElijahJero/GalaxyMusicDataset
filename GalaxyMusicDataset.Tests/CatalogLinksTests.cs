using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Catalog;
using GalaxyMusicDataset.Services.VocaDb;

namespace GalaxyMusicDataset.Tests;

public class CatalogLinksTests
{
    private const string LastFmJson = """
        {
          "track": {
            "name": "Lilac",
            "url": "https://www.last.fm/music/Kyasu/_/Lilac",
            "artist": { "name": "Kyasu", "url": "https://www.last.fm/music/Kyasu" }
          }
        }
        """;

    [Fact]
    public void Builds_musicbrainz_discogs_and_audiodb_urls_from_ids()
    {
        Assert.Equal(
            "https://musicbrainz.org/recording/3f309fb6-fed0-461e-bfd9-c6d7467a4bd4",
            CatalogLinks.MusicBrainzRecording("3f309fb6-fed0-461e-bfd9-c6d7467a4bd4"));
        Assert.Equal(
            "https://musicbrainz.org/release/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            CatalogLinks.MusicBrainzRelease("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.Equal("https://www.discogs.com/release/99", CatalogLinks.DiscogsRelease("99"));
        Assert.Equal("https://www.theaudiodb.com/track/441122", CatalogLinks.TheAudioDbTrack("441122"));
        Assert.Null(CatalogLinks.MusicBrainzRecording(" "));
        Assert.Null(CatalogLinks.DiscogsRelease(null));
    }

    [Fact]
    public void LastFm_prefers_payload_url()
    {
        Assert.Equal(
            "https://www.last.fm/music/Kyasu/_/Lilac",
            CatalogLinks.LastFmTrackFromPayload(LastFmJson));
        Assert.Equal(
            "https://www.last.fm/music/Mori+Calliope/_/Lose-Lose+Days",
            CatalogLinks.LastFmTrackSlug("Mori Calliope", "Lose-Lose Days"));
        Assert.Null(CatalogLinks.LastFmTrackFromPayload("{ not json"));
        Assert.Null(CatalogLinks.LastFmTrackFromPayload(null));
    }

    [Fact]
    public void ForTrack_emits_buttons_only_for_known_identities()
    {
        var links = CatalogLinks.ForTrack(
            "rec-1",
            "rel-1",
            "99",
            "48",
            "7",
            "3",
            "441122",
            LastFmJson,
            "Kyasu",
            "Lilac");

        Assert.Equal(
            [
                new ExternalLink("MusicBrainz", "https://musicbrainz.org/recording/rec-1"),
                new ExternalLink("MusicBrainz release", "https://musicbrainz.org/release/rel-1"),
                new ExternalLink("Discogs", "https://www.discogs.com/release/99"),
                new ExternalLink("VocaDB", VocaDbFamily.SongPageUrl(EnrichmentSource.VocaDb, "48")),
                new ExternalLink("UtaiteDB", VocaDbFamily.SongPageUrl(EnrichmentSource.UtaiteDb, "7")),
                new ExternalLink("TouhouDB", VocaDbFamily.SongPageUrl(EnrichmentSource.TouhouDb, "3")),
                new ExternalLink("Last.fm", "https://www.last.fm/music/Kyasu/_/Lilac"),
                new ExternalLink("TheAudioDB", "https://www.theaudiodb.com/track/441122")
            ],
            links);
    }

    [Fact]
    public void ForTrack_is_empty_without_ids_or_lastfm_payload()
    {
        Assert.Empty(CatalogLinks.ForTrack(
            null, null, null, null, null, null, null, null, "Kyasu", "Lilac"));
    }

    [Fact]
    public void ForTrack_uses_lastfm_slug_when_payload_has_no_url()
    {
        var links = CatalogLinks.ForTrack(
            null, null, null, null, null, null, null,
            """{ "track": { "name": "Lilac" } }""",
            "Kyasu",
            "Lilac");
        Assert.Equal(
            [new ExternalLink("Last.fm", "https://www.last.fm/music/Kyasu/_/Lilac")],
            links);
    }
}
