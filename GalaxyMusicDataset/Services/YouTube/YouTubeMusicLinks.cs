using System.Text.RegularExpressions;

namespace GalaxyMusicDataset.Services.YouTube;

public static class YouTubeMusicLinks
{
    private static readonly Regex VideoId = new(
        @"^[A-Za-z0-9_-]{11}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string OpenUrl(string artist, string title, string? musicVideoUrl)
    {
        var id = TryGetVideoId(musicVideoUrl);
        return id is null ? SearchUrl(artist, title) : WatchUrl(id);
    }

    public static string SearchUrl(string artist, string title)
    {
        var query = string.Join(
            ' ',
            new[] { artist, title }.Where(static s => !string.IsNullOrWhiteSpace(s)));
        return $"https://music.youtube.com/search?q={Uri.EscapeDataString(query.Trim())}";
    }

    public static string WatchUrl(string videoId) => $"https://music.youtube.com/watch?v={videoId}";

    public static string? TryGetVideoId(string? musicVideoUrl)
    {
        if (string.IsNullOrWhiteSpace(musicVideoUrl)
            || !Uri.TryCreate(musicVideoUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;

        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var id = uri.AbsolutePath.Trim('/');
            var slash = id.IndexOf('/');
            if (slash >= 0)
            {
                id = id[..slash];
            }

            return IsVideoId(id) ? id : null;
        }

        if (!IsYouTubeHost(host))
        {
            return null;
        }

        var queryId = QueryValue(uri, "v");
        if (IsVideoId(queryId))
        {
            return queryId;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2
            && segments[0] is "embed" or "shorts" or "v" or "live"
            && IsVideoId(segments[1]))
        {
            return segments[1];
        }

        return null;
    }

    private static bool IsYouTubeHost(string host) =>
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);

    private static string? QueryValue(Uri uri, string key)
    {
        var query = uri.Query;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var name = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }

        return null;
    }

    private static bool IsVideoId(string? value) =>
        !string.IsNullOrEmpty(value) && VideoId.IsMatch(value);
}
