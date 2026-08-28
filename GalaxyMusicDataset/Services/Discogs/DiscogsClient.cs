using System.Net.Http.Headers;
using System.Text.Json;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.Discogs;

public sealed record DiscogsSearchHit(string? Id, string? Title, string? Year, string? Type, string? Uri, string RawJson);

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
        var query = string.IsNullOrWhiteSpace(album)
            ? $"{artist} {track}"
            : $"{artist} {track} {album}";
        var url = $"https://api.discogs.com/database/search?q={Uri.EscapeDataString(query)}&type=release&per_page=5";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Authorization = new AuthenticationHeaderValue("Discogs", $"token={Token}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = await recorder.SendAsync(http, request, "Discogs", RateLimiter, cancellationToken);
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
            first.GetRawText());
        return (hit, json);
    }
}
