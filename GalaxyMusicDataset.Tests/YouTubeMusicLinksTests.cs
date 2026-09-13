using GalaxyMusicDataset.Services.YouTube;

namespace GalaxyMusicDataset.Tests;

public class YouTubeMusicLinksTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&feature=share", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void TryGetVideoId_parses_youtube_urls(string url, string expected)
    {
        Assert.Equal(expected, YouTubeMusicLinks.TryGetVideoId(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://vimeo.com/12345")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    public void TryGetVideoId_rejects_non_youtube(string? url)
    {
        Assert.Null(YouTubeMusicLinks.TryGetVideoId(url));
    }

    [Fact]
    public void OpenUrl_uses_watch_when_video_id_is_known()
    {
        var url = YouTubeMusicLinks.OpenUrl(
            "Mori Calliope",
            "Lose-Lose Days",
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.Equal("https://music.youtube.com/watch?v=dQw4w9WgXcQ", url);
    }

    [Fact]
    public void OpenUrl_falls_back_to_search()
    {
        var url = YouTubeMusicLinks.OpenUrl("Mori Calliope", "Lose-Lose Days", null);
        Assert.Equal(
            "https://music.youtube.com/search?q=Mori%20Calliope%20Lose-Lose%20Days",
            url);
    }
}
