using System.Text.Json;
using GalaxyMusicDataset.Services.LastFm;

namespace GalaxyMusicDataset.Tests;

public class LastFmParsingTests
{
    [Fact]
    public void Parses_dated_track()
    {
        var json = """
            {
              "artist": { "mbid": "a", "#text": "fourfolium" },
              "name": "Now Loading!!!!",
              "mbid": "3f309fb6-fed0-461e-bfd9-c6d7467a4bd4",
              "album": { "mbid": "", "#text": "TVアニメ「NEW GAME!」エンディングテーマ" },
              "date": { "uts": "1787928554", "#text": "x" }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var track = LastFmClient.ParseTrack(doc.RootElement);
        Assert.False(track.IsNowPlaying);
        Assert.Equal("fourfolium", track.ArtistName);
        Assert.Equal("Now Loading!!!!", track.TrackName);
        Assert.Equal(1787928554, track.TimestampUnix);
        Assert.Equal("3f309fb6-fed0-461e-bfd9-c6d7467a4bd4", track.TrackMbid);
    }

    [Fact]
    public void Parses_now_playing_without_timestamp()
    {
        var json = """
            {
              "artist": { "#text": "Kyasu" },
              "name": "Lilac",
              "album": { "#text": "Lilac" },
              "@attr": { "nowplaying": "true" }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var track = LastFmClient.ParseTrack(doc.RootElement);
        Assert.True(track.IsNowPlaying);
        Assert.Null(track.TimestampUnix);
    }
}
