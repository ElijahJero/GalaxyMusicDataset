using System.Net;
using System.Text;
using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.Scrobbling;

namespace GalaxyMusicDataset.Services.Maloja;

public sealed record MalojaAlbumArt(string? ImageUrl, int? AlbumId, string RawJson);

public sealed class MalojaClient(
    HttpClient http,
    ApiCallRecorder recorder) : IScrobbleHistoryClient
{
    public static readonly ApiRateLimiter RateLimiter = new(TimeSpan.FromMilliseconds(MalojaOptions.DefaultMinIntervalMs));

    public required string BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public string UserAgent { get; init; } = "GalaxyMusicDataset/0.1";

    public ScrobbleProvider Provider => ScrobbleProvider.Maloja;
    public string DisplayName => "Maloja";
    public bool SupportsDeletionReconcile => true;

    public async Task<ScrobbleUserInfo> GetUserInfoAsync(CancellationToken cancellationToken)
    {
        var amount = await GetNumScrobblesAsync(cancellationToken);
        string name = "Maloja";
        try
        {
            var json = await GetJsonAsync(Api("serverinfo"), cancellationToken);
            using var doc = JsonDocument.Parse(json);
            name = doc.RootElement.GetPropertyString("name") ?? name;
        }
        catch (JsonApiException)
        {
            // serverinfo is optional; playcount alone is enough for status.
        }

        return new ScrobbleUserInfo(name, amount, RegisteredUtc: null);
    }

    public async Task<long> GetNumScrobblesAsync(CancellationToken cancellationToken)
    {
        var json = await GetJsonAsync(Api("numscrobbles"), cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetPropertyLong("amount") ?? 0;
    }

    public async Task<PlayWindowResult> GetPlaysWindowAsync(
        long? fromUnix,
        long? toUnix,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var plays = new List<IncomingPlay>();
        var page = 0;
        var perPage = Math.Clamp(pageSize, 1, 1000);
        var reportedTotal = -1;
        string? fromDate = fromUnix is > 0
            ? DateTimeOffset.FromUnixTimeSeconds(fromUnix.Value).UtcDateTime.ToString("yyyy/MM/dd")
            : null;
        string? untilDate = toUnix is > 0
            ? DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, toUnix.Value - 1)).UtcDateTime.ToString("yyyy/MM/dd")
            : null;

        while (true)
        {
            var query = new Dictionary<string, string?>
            {
                ["page"] = page.ToString(),
                ["perpage"] = perPage.ToString()
            };
            if (fromDate is not null)
            {
                query["from"] = fromDate;
            }

            if (untilDate is not null)
            {
                query["until"] = untilDate;
            }

            var json = await GetJsonAsync(Api("scrobbles", query), cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            ThrowIfError(root);

            if (!root.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var batch = list.EnumerateArray().Select(ParseScrobble).Where(p => p is not null).Cast<IncomingPlay>().ToList();
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var play in batch)
            {
                if (fromUnix is > 0 && play.TimestampUnix < fromUnix)
                {
                    continue;
                }

                if (toUnix is > 0 && play.TimestampUnix >= toUnix)
                {
                    continue;
                }

                plays.Add(play);
            }

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
            if (page > 10_000)
            {
                return new PlayWindowResult(plays, plays.Count, page, false,
                    "Maloja pagination exceeded 10000 pages; window may be incomplete.");
            }
        }

        reportedTotal = plays.Count;
        return new PlayWindowResult(plays, reportedTotal, page + 1, true, null);
    }

    public async Task<MalojaAlbumArt?> FindAlbumArtAsync(
        string? artist,
        string? albumTitle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(albumTitle))
        {
            return null;
        }

        var query = albumTitle.Trim();
        if (!string.IsNullOrWhiteSpace(artist))
        {
            query = artist.Trim() + " " + query;
        }

        var json = await GetJsonAsync(
            Api("search", new Dictionary<string, string?> { ["query"] = query, ["max"] = "12" }),
            cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("albums", out var albums) || albums.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? best = null;
        var bestScore = -1;
        var wantTitle = Normalize(albumTitle);
        var wantArtist = Normalize(artist);
        foreach (var item in albums.EnumerateArray())
        {
            if (!item.TryGetProperty("album", out var album))
            {
                continue;
            }

            var title = album.GetPropertyString("albumtitle") ?? "";
            var artists = ReadArtistList(album);
            var score = 0;
            if (Normalize(title) == wantTitle)
            {
                score += 10;
            }
            else if (Normalize(title).Contains(wantTitle, StringComparison.Ordinal) ||
                     wantTitle.Contains(Normalize(title), StringComparison.Ordinal))
            {
                score += 4;
            }

            if (!string.IsNullOrEmpty(wantArtist) &&
                artists.Any(a => Normalize(a) == wantArtist || Normalize(a).Contains(wantArtist, StringComparison.Ordinal)))
            {
                score += 5;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        if (best is null || bestScore < 4)
        {
            return null;
        }

        var imagePath = best.Value.GetPropertyString("image");
        var albumId = ParseAlbumId(imagePath);
        if (string.IsNullOrWhiteSpace(imagePath) && albumId is null)
        {
            return null;
        }

        var absolute = ToAbsoluteUrl(imagePath ?? $"/image?album_id={albumId}");
        var resolved = await ResolveImageUrlAsync(absolute, cancellationToken) ?? absolute;
        if (IsPlaceholder(resolved))
        {
            return null;
        }

        return new MalojaAlbumArt(resolved, albumId, best.Value.GetRawText());
    }

    public async Task<string?> ResolveImageUrlAsync(string imageUrl, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            await RateLimiter.WaitAsync(cancellationToken);
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                return location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(BaseUrl.TrimEnd('/') + "/"), location).ToString();
            }

            if (response.IsSuccessStatusCode)
            {
                // Prefer the post-redirect URI when HttpClient followed Maloja's 307.
                return response.RequestMessage?.RequestUri?.ToString() ?? imageUrl;
            }

            return null;
        }

        return imageUrl;
    }

    public static IncomingPlay? ParseScrobble(JsonElement item)
    {
        var time = item.GetPropertyLong("time");
        if (time is null or <= 0)
        {
            return null;
        }

        if (!item.TryGetProperty("track", out var track))
        {
            return null;
        }

        var title = track.GetPropertyString("title") ?? "";
        var artists = ReadArtistList(track);
        var artistName = artists.Count > 0 ? string.Join(", ", artists) : "";
        string? albumName = null;
        if (track.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
        {
            albumName = EmptyToNull(album.GetPropertyString("albumtitle"));
        }

        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new IncomingPlay(
            ScrobbleProvider.Maloja,
            artistName,
            title,
            albumName,
            time,
            null,
            null,
            null,
            false,
            item.GetRawText());
    }

    public static bool IsPlaceholder(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        return url.Contains("placeholder_", StringComparison.OrdinalIgnoreCase)
               || url.Contains("generative-placeholders", StringComparison.OrdinalIgnoreCase)
               || url.Contains("/static/svg/placeholder", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadArtistList(JsonElement parent)
    {
        if (!parent.TryGetProperty("artists", out var artists))
        {
            return [];
        }

        if (artists.ValueKind == JsonValueKind.String)
        {
            var s = artists.GetString();
            return string.IsNullOrWhiteSpace(s) ? [] : [s];
        }

        if (artists.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return artists.EnumerateArray()
            .Select(a => a.GetFlexibleText() ?? a.GetString() ?? "")
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();
    }

    private static int? ParseAlbumId(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        const string marker = "album_id=";
        var idx = imagePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var rest = imagePath[(idx + marker.Length)..];
        var end = rest.IndexOfAny(['&', '#']);
        if (end >= 0)
        {
            rest = rest[..end];
        }

        return int.TryParse(rest, out var id) ? id : null;
    }

    private string ToAbsoluteUrl(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _))
        {
            return pathOrUrl;
        }

        return BaseUrl.TrimEnd('/') + (pathOrUrl.StartsWith('/') ? pathOrUrl : "/" + pathOrUrl);
    }

    private string Api(string endpoint, Dictionary<string, string?>? query = null)
    {
        var url = new StringBuilder(BaseUrl.TrimEnd('/'))
            .Append("/apis/mlj_1/")
            .Append(endpoint.TrimStart('/'));
        var pairs = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            pairs.Add("key=" + Uri.EscapeDataString(ApiKey));
        }

        if (query is not null)
        {
            foreach (var (key, value) in query)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
                }
            }
        }

        if (pairs.Count > 0)
        {
            url.Append('?').Append(string.Join("&", pairs));
        }

        return url.ToString();
    }

    private async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return await recorder.SendAsync(http, request, "Maloja", RateLimiter, cancellationToken);
    }

    private static void ThrowIfError(JsonElement root)
    {
        var status = root.GetPropertyString("status");
        if (status is "error" or "failure")
        {
            string? message = root.GetPropertyString("desc");
            if (root.TryGetProperty("error", out var error))
            {
                message ??= error.GetPropertyString("desc") ?? error.ToString();
            }

            throw new JsonApiException("Maloja", message ?? "Maloja request failed.");
        }
    }

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
