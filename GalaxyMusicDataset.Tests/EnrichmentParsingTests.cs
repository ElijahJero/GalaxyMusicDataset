using System.Text.Json;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Discogs;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.MusicBrainz;
using GalaxyMusicDataset.Services.TheAudioDb;

namespace GalaxyMusicDataset.Tests;

public class EnrichmentParsingTests
{
    [Fact]
    public void Discogs_search_reads_cover_and_year()
    {
        var json = """
            {
              "results": [
                {
                  "id": 123,
                  "title": "fourfolium - Now Loading!!!!",
                  "year": "2016",
                  "type": "release",
                  "uri": "/releases/123",
                  "cover_image": "https://example.com/cover.jpg",
                  "genre": ["Pop", "Stage & Screen"],
                  "style": ["Anison"]
                }
              ]
            }
            """;
        var (hit, _) = DiscogsClient.ParseSearch(json);
        Assert.NotNull(hit);
        Assert.Equal("123", hit!.Id);
        Assert.Equal("2016", hit.Year);
        Assert.Equal("https://example.com/cover.jpg", hit.CoverUrl);
    }

    [Fact]
    public void Discogs_release_prefers_primary_image()
    {
        var json = """
            {
              "id": 99,
              "title": "Now Loading!!!!",
              "year": 2016,
              "uri": "https://www.discogs.com/release/99",
              "genres": ["Pop"],
              "styles": ["Anison"],
              "images": [
                { "type": "secondary", "uri150": "https://example.com/sec.jpg" },
                { "type": "primary", "uri150": "https://example.com/pri.jpg" }
              ]
            }
            """;
        var release = DiscogsClient.ParseRelease(json);
        Assert.NotNull(release);
        Assert.Equal(2016, release!.Year);
        Assert.Equal("https://example.com/pri.jpg", release.CoverUrl);
        Assert.Contains("Pop", release.Genres);
        Assert.Contains("Anison", release.Styles);
    }

    [Fact]
    public void AudioDb_parses_video_wiki_and_mbid()
    {
        var json = """
            {
              "track": [{
                "idTrack": "441122",
                "strTrack": "Now Loading!!!!",
                "strArtist": "fourfolium",
                "strAlbum": "Now Loading!!!!",
                "idAlbum": "55",
                "intDuration": "237000",
                "strGenre": "Anime",
                "strMood": "Happy",
                "strStyle": "J-Pop",
                "strDescriptionEN": "Opening theme.",
                "strTrackThumb": "https://example.com/thumb.jpg",
                "strMusicVid": "https://youtube.com/watch?v=abc",
                "strMusicBrainzID": "3f309fb6-fed0-461e-bfd9-c6d7467a4bd4"
              }]
            }
            """;
        var (hit, _) = TheAudioDbClient.ParseSearch(json);
        Assert.NotNull(hit);
        Assert.Equal("441122", hit!.Id);
        Assert.Equal(237000, hit.DurationMs);
        Assert.Equal("Anime", hit.Genre);
        Assert.Equal("https://youtube.com/watch?v=abc", hit.MusicVideoUrl);
        Assert.Equal("3f309fb6-fed0-461e-bfd9-c6d7467a4bd4", hit.MusicBrainzId);
        Assert.Equal("Opening theme.", hit.Description);
    }

    [Fact]
    public void LastFm_track_info_reads_wiki_and_cover()
    {
        var json = """
            {
              "track": {
                "name": "Lilac",
                "mbid": "mb-track",
                "url": "https://www.last.fm/music/Kyasu/_/Lilac",
                "duration": "180000",
                "artist": { "name": "Kyasu", "mbid": "mb-artist", "url": "https://www.last.fm/music/Kyasu" },
                "album": {
                  "title": "Lilac",
                  "mbid": "mb-album",
                  "image": [
                    { "#text": "https://example.com/small.png", "size": "small" },
                    { "#text": "https://example.com/xl.png", "size": "extralarge" }
                  ]
                },
                "wiki": { "summary": "A song.<a href=\"https://last.fm\">Read more</a>" },
                "toptags": { "tag": [{ "name": "j-pop", "count": "12" }] }
              }
            }
            """;
        var info = LastFmClient.ParseTrackInfo(json);
        Assert.NotNull(info);
        Assert.Equal("mb-track", info!.Mbid);
        Assert.Equal(180000, info.DurationMs);
        Assert.Equal("https://example.com/xl.png", info.AlbumImageUrl);
        Assert.Equal("https://www.last.fm/music/Kyasu", info.ArtistUrl);
        Assert.Equal("A song.", MetadataEnrichmentService.StripWikiMarkup(info.WikiSummary));
        Assert.Contains(info.Tags, t => t.Name == "j-pop");
    }

    [Fact]
    public void MusicBrainz_recording_details_read_isrc_tags_and_cover()
    {
        var json = """
            {
              "id": "rec-1",
              "title": "Lilac",
              "length": 181000,
              "isrcs": ["JPB601234567"],
              "genres": [{ "name": "j-pop", "count": 2 }],
              "tags": [{ "name": "anime", "count": 4 }],
              "releases": [{ "id": "rel-1", "title": "Lilac", "date": "2024-05-01" }]
            }
            """;
        var details = MusicBrainzClient.ParseRecordingDetails(json);
        Assert.NotNull(details);
        Assert.Equal("JPB601234567", details!.FirstIsrc);
        Assert.Equal(2024, details.ReleaseYear);
        Assert.Equal("rel-1", details.ReleaseMbid);
        Assert.True(MusicBrainzClient.HasRecordingDetails(json));
        Assert.False(MusicBrainzClient.HasRecordingDetails("""{"Mbid":"rec-1","Title":"Lilac"}"""));

        var coverJson = """
            {
              "images": [
                { "front": false, "image": "https://example.com/back.jpg" },
                { "front": true, "thumbnails": { "small": "https://example.com/front.jpg" }, "image": "https://example.com/front-full.jpg" }
              ]
            }
            """;
        Assert.Equal("https://example.com/front.jpg", MusicBrainzClient.ParseCoverArtFrontUrl(coverJson));

        var track = new Track { Title = "Lilac" };
        MetadataEnrichmentService.ApplyRecordingDetails(track, details);
        Assert.Equal("JPB601234567", track.Isrc);
        Assert.Equal(181000, track.DurationMs);
    }
}
