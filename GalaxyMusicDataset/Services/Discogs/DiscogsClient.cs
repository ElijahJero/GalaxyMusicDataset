using System.Net.Http.Headers;
using System.Text.Json;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.Discogs;

public sealed record DiscogsSearchHit(string? Id, string? Title, string? Year, string? Type, string? Uri, string? CoverUrl, string RawJson);

public sealed record DiscogsRelease(
    string Id,
    string? Title,
    int? Year,
    string? CoverUrl,
    string? Uri,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Styles,
    string RawJson);

public sealed class DiscogsClient(HttpClient http, ApiCallRecorder recorder)
{
    public static readonly ApiRateLimiter RateLimiter = new(TimeSpan.FromMilliseconds(1100));

    public required string Token { get; init; }
    public required string UserAgent { get; init; }

    public async Task<(DiscogsSearchHit? Best, string RawJson)?> SearchAsync(
        string artist,
        string track,
        string? album,
        CancellationToken cancellationToken)
    {
        var url =
            "https://api.discogs.com/database/search?type=release&per_page=5" +
            $"&artist={Uri.EscapeDataString(artist)}" +
            $"&track={Uri.EscapeDataString(track)}";
        if (!string.IsNullOrWhiteSpace(album))
        {
            url += $"&release_title={Uri.EscapeDataString(album)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);
        var json = await recorder.SendAsync(http, request, "Discogs", RateLimiter, cancellationToken);
        return ParseSearch(json);
    }

    public async Task<DiscogsRelease?> GetReleaseAsync(string releaseId, CancellationToken cancellationToken)
    {
        var url = $"https://api.discogs.com/releases/{Uri.EscapeDataString(releaseId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);
        var json = await recorder.SendAsync(http, request, "Discogs", RateLimiter, cancellationToken);
        return ParseRelease(json);
    }

    public static (DiscogsSearchHit? Best, string RawJson) ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
        {
            return (null, json);
        }

        var first = results[0];
        var hit = new DiscogsSearchHit(
            first.GetPropertyString("id"),
            first.GetPropertyString("title"),
            first.GetPropertyString("year"),
            first.GetPropertyString("type"),
            first.GetPropertyString("uri"),
            first.GetPropertyString("cover_image") ?? first.GetPropertyString("thumb"),
            first.GetRawText());
        return (hit, json);
    }

    public static DiscogsRelease? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.GetPropertyString("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string? cover = null;
        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in images.EnumerateArray())
            {
                var type = image.GetPropertyString("type");
                var uri = image.GetPropertyString("uri150")
                          ?? image.GetPropertyString("uri")
                          ?? image.GetPropertyString("resource_url");
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                cover = uri;
                if (string.Equals(type, "primary", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        var uriPath = root.GetPropertyString("uri") ?? root.GetPropertyString("resource_url");
        return new DiscogsRelease(
            id,
            root.GetPropertyString("title"),
            root.GetPropertyInt("year"),
            cover,
            uriPath,
            ReadStringList(root, "genres"),
            ReadStringList(root, "styles"),
            json);
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Authorization = new AuthenticationHeaderValue("Discogs", $"token={Token}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add(text);
                }
            }
        }

        return list;
    }
}
