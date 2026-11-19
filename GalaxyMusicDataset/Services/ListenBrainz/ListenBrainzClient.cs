using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.Scrobbling;

namespace GalaxyMusicDataset.Services.ListenBrainz;

public sealed class ListenBrainzClient(
    HttpClient http,
    ApiCallRecorder recorder) : IScrobbleHistoryClient
{
    public static readonly ApiRateLimiter RateLimiter =
        new(TimeSpan.FromMilliseconds(ListenBrainzOptions.DefaultMinIntervalMs));

    public required string Username { get; init; }
    public string? Token { get; init; }
    public string BaseUrl { get; init; } = ListenBrainzOptions.DefaultBaseUrl;
    public string UserAgent { get; init; } = "GalaxyMusicDataset/0.1";

    public ScrobbleProvider Provider => ScrobbleProvider.ListenBrainz;
    public string DisplayName => "ListenBrainz";

    /// <summary>
    /// ListenBrainz does not expose a reliable "deleted listens" view for reconcile deletes.
    /// </summary>
    public bool SupportsDeletionReconcile => false;

    public async Task<ScrobbleUserInfo> GetUserInfoAsync(CancellationToken cancellationToken)
    {
        var json = await GetJsonAsync($"1/user/{Uri.EscapeDataString(Username)}/listen-count", cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var count = 0L;
        if (doc.RootElement.TryGetProperty("payload", out var payload))
        {
            count = payload.GetPropertyLong("count") ?? 0;
        }

        return new ScrobbleUserInfo(Username, count, RegisteredUtc: null);
    }

    public async Task<PlayWindowResult> GetPlaysWindowAsync(
        long? fromUnix,
        long? toUnix,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var plays = new List<IncomingPlay>();
        var count = Math.Clamp(pageSize, 1, 1000);
        long? maxTs = toUnix;
        var pages = 0;

        // ListenBrainz returns newest-first. Paginate with max_ts (exclusive).
        // min_ts cannot be combined with max_ts, so filter fromUnix client-side.
        while (true)
        {
            var query = new Dictionary<string, string?> { ["count"] = count.ToString() };
            if (maxTs is not null)
            {
                query["max_ts"] = maxTs.Value.ToString();
            }

            var json = await GetJsonAsync(
                $"1/user/{Uri.EscapeDataString(Username)}/listens",
                query,
                cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("listens", out var listens) ||
                listens.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var batch = listens.EnumerateArray().Select(ParseListen).Where(p => p is not null).Cast<IncomingPlay>().ToList();
            pages++;
            if (batch.Count == 0)
            {
                break;
            }

            var reachedFloor = false;
            foreach (var play in batch)
            {
                if (fromUnix is > 0 && play.TimestampUnix <= fromUnix)
                {
                    reachedFloor = true;
                    continue;
                }

                if (toUnix is > 0 && play.TimestampUnix >= toUnix)
                {
                    continue;
                }

                plays.Add(play);
            }

            var oldest = batch.Min(p => p.TimestampUnix!.Value);
            if (reachedFloor || batch.Count < count || (fromUnix is > 0 && oldest <= fromUnix))
            {
                break;
            }

            maxTs = oldest;
            if (pages > 10_000)
            {
                return new PlayWindowResult(plays, plays.Count, pages, false,
                    "ListenBrainz pagination exceeded 10000 pages; window may be incomplete.");
            }
        }

        return new PlayWindowResult(plays, plays.Count, Math.Max(1, pages), true, null);
    }

    public static IncomingPlay? ParseListen(JsonElement item)
    {
        var listenedAt = item.GetPropertyLong("listened_at");
        if (listenedAt is null or <= 0)
        {
            return null;
        }

        if (!item.TryGetProperty("track_metadata", out var meta))
        {
            return null;
        }

        var artist = meta.GetPropertyString("artist_name") ?? "";
        var track = meta.GetPropertyString("track_name") ?? "";
        var album = EmptyToNull(meta.GetPropertyString("release_name"));
        string? trackMbid = null;
        string? artistMbid = null;
        string? albumMbid = null;
        if (meta.TryGetProperty("additional_info", out var extra) && extra.ValueKind == JsonValueKind.Object)
        {
            trackMbid = EmptyToNull(extra.GetPropertyString("recording_mbid") ?? extra.GetPropertyString("track_mbid"));
            albumMbid = EmptyToNull(extra.GetPropertyString("release_mbid"));
            if (extra.TryGetProperty("artist_mbids", out var artistMbids) &&
                artistMbids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in artistMbids.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                    {
                        artistMbid = id.GetString();
                        break;
                    }
                }
            }

            artistMbid ??= EmptyToNull(extra.GetPropertyString("artist_mbid"));
        }

        if (meta.TryGetProperty("mbid_mapping", out var mapping) && mapping.ValueKind == JsonValueKind.Object)
        {
            trackMbid ??= EmptyToNull(mapping.GetPropertyString("recording_mbid"));
            albumMbid ??= EmptyToNull(mapping.GetPropertyString("release_mbid"));
            if (artistMbid is null &&
                mapping.TryGetProperty("artist_mbids", out var mappedArtists) &&
                mappedArtists.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in mappedArtists.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                    {
                        artistMbid = id.GetString();
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track))
        {
            return null;
        }

        return new IncomingPlay(
            ScrobbleProvider.ListenBrainz,
            artist,
            track,
            album,
            listenedAt,
            trackMbid,
            artistMbid,
            albumMbid,
            false,
            item.GetRawText());
    }

    private async Task<string> GetJsonAsync(
        string path,
        Dictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        var url = BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        if (query is { Count: > 0 })
        {
            var pairs = query
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
            url += "?" + string.Join("&", pairs);
        }

        return await GetJsonAsync(url, cancellationToken);
    }

    private async Task<string> GetJsonAsync(string pathOrUrl, CancellationToken cancellationToken)
    {
        var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : BaseUrl.TrimEnd('/') + "/" + pathOrUrl.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (!string.IsNullOrWhiteSpace(Token))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Token " + Token.Trim());
        }

        return await recorder.SendAsync(http, request, "ListenBrainz", RateLimiter, cancellationToken);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
