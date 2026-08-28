using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Tests;

public class MusicBrainzEndpointTests
{
    [Theory]
    [InlineData(null, "https://musicbrainz.org/ws/2")]
    [InlineData("", "https://musicbrainz.org/ws/2")]
    [InlineData("   ", "https://musicbrainz.org/ws/2")]
    [InlineData("https://musicbrainz.org", "https://musicbrainz.org/ws/2")]
    [InlineData("https://musicbrainz.org/", "https://musicbrainz.org/ws/2")]
    [InlineData("http://localhost:5000", "http://localhost:5000/ws/2")]
    [InlineData("localhost:5000", "http://localhost:5000/ws/2")]
    [InlineData("http://localhost:5000/ws/2", "http://localhost:5000/ws/2")]
    [InlineData("http://localhost:5000/ws/2/", "http://localhost:5000/ws/2")]
    [InlineData("http://192.168.1.10:5000/", "http://192.168.1.10:5000/ws/2")]
    public void Web_service_root_normalizes_mirror_urls(string? input, string expected)
    {
        Assert.Equal(expected, MusicBrainzEndpoints.WebServiceRoot(input));
    }

    [Fact]
    public void Public_host_keeps_slow_interval()
    {
        var options = new MusicBrainzOptions();
        Assert.True(options.UsesPublicWebService);
        Assert.True(options.UsesPublicCoverArt);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), options.WebServiceMinInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), options.CoverArtMinInterval);
        Assert.StartsWith("https://musicbrainz.org/ws/2/recording/", options.RecordingSearchUrl("test"));
        Assert.Equal(
            "https://coverartarchive.org/release/rel-1",
            options.ReleaseCoverArtUrl("rel-1"));
    }

    [Fact]
    public void Local_host_speeds_up_web_service_but_not_public_cover_art()
    {
        var options = new MusicBrainzOptions { BaseUrl = "http://localhost:5000" };
        Assert.False(options.UsesPublicWebService);
        Assert.True(options.UsesPublicCoverArt);
        Assert.Equal(TimeSpan.FromMilliseconds(50), options.WebServiceMinInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), options.CoverArtMinInterval);
        Assert.Equal(
            "http://localhost:5000/ws/2/recording/mbid-1?inc=artist-credits+releases+aliases+tags+isrcs+genres&fmt=json",
            options.RecordingLookupUrl("mbid-1"));
        Assert.Equal(
            "http://localhost:5000/ws/2/artist/artist-1?inc=aliases&fmt=json",
            options.ArtistLookupUrl("artist-1"));
    }

    [Fact]
    public void Explicit_zero_interval_does_not_speed_up_public_cover_art()
    {
        var options = new MusicBrainzOptions
        {
            BaseUrl = "http://localhost:5000",
            MinIntervalMs = 0
        };
        Assert.Equal(TimeSpan.Zero, options.WebServiceMinInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), options.CoverArtMinInterval);
    }

    [Fact]
    public void Local_cover_art_uses_fast_interval()
    {
        var options = new MusicBrainzOptions
        {
            BaseUrl = "http://mb:5000",
            CoverArtBaseUrl = "http://caa:8080"
        };
        Assert.False(options.UsesPublicCoverArt);
        Assert.Equal(TimeSpan.FromMilliseconds(50), options.CoverArtMinInterval);
        Assert.Equal("http://caa:8080/release/abc", options.ReleaseCoverArtUrl("abc"));
    }

    [Fact]
    public void Search_url_uses_custom_host()
    {
        var options = new MusicBrainzOptions { BaseUrl = "http://localhost:5000" };
        var url = options.RecordingSearchUrl("recording:\"x\" AND artist:\"y\"");
        Assert.StartsWith("http://localhost:5000/ws/2/recording/", url);
        Assert.Contains("fmt=json", url, StringComparison.Ordinal);
        Assert.Contains("limit=8", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Beta_musicbrainz_is_still_treated_as_public()
    {
        var options = new MusicBrainzOptions { BaseUrl = "https://beta.musicbrainz.org" };
        Assert.True(options.UsesPublicWebService);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), options.WebServiceMinInterval);
        Assert.StartsWith("https://beta.musicbrainz.org/ws/2/", options.RecordingLookupUrl("x"));
    }

    [Fact]
    public async Task SetMinInterval_applies_to_the_gap_between_calls()
    {
        var limiter = new ApiRateLimiter(TimeSpan.FromSeconds(5));
        limiter.SetMinInterval(TimeSpan.FromMilliseconds(40));
        Assert.Equal(TimeSpan.FromMilliseconds(40), limiter.MinInterval);
        await limiter.WaitAsync(CancellationToken.None);
        var started = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync(CancellationToken.None);
        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(20), started.Elapsed.ToString());
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), started.Elapsed.ToString());
    }
}
