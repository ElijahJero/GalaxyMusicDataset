using System.Text.Json;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.LastFm;

public sealed record LastFmUserInfo(string Name, long Playcount, DateTimeOffset? RegisteredUtc);

public sealed record LastFmRecentTrack(
    string ArtistName,
    string TrackName,
    string? AlbumName,
    long? TimestampUnix,
    string? TrackMbid,
    string? ArtistMbid,
    string? AlbumMbid,
    bool IsNowPlaying,
    string RawJson);

public sealed record LastFmWindowResult(
    IReadOnlyList<LastFmRecentTrack> Tracks,
    int ReportedTotal,
    int PageCount,
    bool Ok,
    string? Warning);

public sealed record LastFmTrackInfo(
    string? Mbid,
    int? DurationMs,
    string? AlbumTitle,
    string? AlbumMbid,
    string? ArtistMbid,
    IReadOnlyList<LastFmTag> Tags,
    string RawJson);

public sealed record LastFmTag(string Name, int Weight);

public sealed class LastFmClient(
    HttpClient http,
    ApiCallRecorder recorder)
{
    public static readonly ApiRateLimiter RateLimiter = new(TimeSpan.FromMilliseconds(250));

    public required string ApiKey { get; init; }
    public required string Username { get; init; }
    public string UserAgent { get; init; } = "GalaxyMusicDataset/0.1";

    public async Task<LastFmUserInfo> GetUserInfoAsync(CancellationToken cancellationToken)
    {
        var url = Build("user.getInfo", new Dictionary<string, string?> { ["user"] = Username });
        var json = await GetJsonAsync(url, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        ThrowIfError(root);
        var user = root.GetProperty("user");
        var playcount = user.GetPropertyLong("playcount") ?? 0;
        DateTimeOffset? registered = null;
        if (user.TryGetProperty("registered", out var registeredEl))
        {
            var unix = registeredEl.GetPropertyLong("unixtime") ?? registeredEl.GetPropertyLong("#text");
            if (unix is > 0)
            {
                registered = DateTimeOffset.FromUnixTimeSeconds(unix.Value);
            }
        }

        return new LastFmUserInfo(user.GetPropertyString("name") ?? Username, playcount, registered);
    }

    public async Task<LastFmWindowResult> GetRecentTracksWindowAsync(
        long? fromUnix,
        long? toUnix,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var tracks = new List<LastFmRecentTrack>();
        var page = 1;
        var totalPages = 1;
        var reportedTotal = 0;

        while (page <= totalPages)
        {
            var query = new Dictionary<string, string?>
            {
                ["user"] = Username,
                ["limit"] = pageSize.ToString(),
                ["page"] = page.ToString(),
                ["extended"] = "0"
            };
            if (fromUnix is not null)
            {
                query["from"] = fromUnix.Value.ToString();
            }

            if (toUnix is not null)
            {
                query["to"] = toUnix.Value.ToString();
            }

            var json = await GetJsonAsync(Build("user.getRecentTracks", query), cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            ThrowIfError(root);
            if (!root.TryGetProperty("recenttracks", out var recent))
            {
                break;
            }

            if (recent.TryGetProperty("@attr", out var attr))
            {
                reportedTotal = attr.GetPropertyInt("total") ?? reportedTotal;
                totalPages = Math.Max(1, attr.GetPropertyInt("totalPages") ?? 1);
            }

            if (recent.TryGetProperty("track", out var trackEl))
            {
                foreach (var item in trackEl.EnumerateFlexibleArray())
                {
                    tracks.Add(ParseTrack(item));
                }
            }

            page++;
        }

        var dated = tracks.Where(t => !t.IsNowPlaying && t.TimestampUnix is not null).ToList();
        string? warning = null;
        var ok = true;
        if (reportedTotal > 0 && Math.Abs(dated.Count - reportedTotal) > 1)
        {
            ok = false;
            warning = $"Last.fm reported {reportedTotal} tracks but parsed {dated.Count} (plus {tracks.Count - dated.Count} now-playing/undated).";
        }

        return new LastFmWindowResult(tracks, reportedTotal, totalPages, ok, warning);
    }

    public async Task<LastFmTrackInfo?> GetTrackInfoAsync(string artist, string track, CancellationToken cancellationToken)
    {
        var url = Build("track.getInfo", new Dictionary<string, string?>
        {
            ["artist"] = artist,
            ["track"] = track,
            ["username"] = Username,
            ["autocorrect"] = "0"
        });

        try
        {
            var json = await GetJsonAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _))
            {
                return null;
            }

            if (!root.TryGetProperty("track", out var t))
            {
                return null;
            }

            var duration = t.GetPropertyInt("duration");
            if (duration is 0)
            {
                duration = null;
            }

            string? albumTitle = null;
            string? albumMbid = null;
            if (t.TryGetProperty("album", out var album))
            {
                albumTitle = album.GetPropertyString("title");
                albumMbid = EmptyToNull(album.GetPropertyString("mbid"));
            }

            string? artistMbid = null;
            if (t.TryGetProperty("artist", out var artistEl))
            {
                artistMbid = EmptyToNull(artistEl.GetPropertyString("mbid"));
            }

            var tags = new List<LastFmTag>();
            if (t.TryGetProperty("toptags", out var topTags) && topTags.TryGetProperty("tag", out var tagEl))
            {
                foreach (var tag in tagEl.EnumerateFlexibleArray())
                {
                    var name = tag.GetPropertyString("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    tags.Add(new LastFmTag(name, tag.GetPropertyInt("count") ?? 0));
                }
            }

            return new LastFmTrackInfo(
                EmptyToNull(t.GetPropertyString("mbid")),
                duration,
                albumTitle,
                albumMbid,
                artistMbid,
                tags,
                json);
        }
        catch (JsonApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public static LastFmRecentTrack ParseTrack(JsonElement item)
    {
        var nowPlaying = false;
        if (item.TryGetProperty("@attr", out var attr) && attr.TryGetProperty("nowplaying", out var np))
        {
            nowPlaying = np.ValueKind == JsonValueKind.String &&
                         string.Equals(np.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        long? uts = null;
        if (item.TryGetProperty("date", out var date))
        {
            uts = date.GetPropertyLong("uts");
        }

        string artistName = "";
        string? artistMbid = null;
        if (item.TryGetProperty("artist", out var artist))
        {
            artistName = artist.GetFlexibleText() ?? "";
            artistMbid = EmptyToNull(artist.GetPropertyString("mbid"));
        }

        string? albumName = null;
        string? albumMbid = null;
        if (item.TryGetProperty("album", out var album))
        {
            albumName = EmptyToNull(album.GetFlexibleText());
            albumMbid = EmptyToNull(album.GetPropertyString("mbid"));
        }

        return new LastFmRecentTrack(
            artistName,
            item.GetPropertyString("name") ?? "",
            albumName,
            uts,
            EmptyToNull(item.GetPropertyString("mbid")),
            artistMbid,
            albumMbid,
            nowPlaying,
            item.GetRawText());
    }

    private async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return await recorder.SendAsync(http, request, "Last.fm", RateLimiter, cancellationToken);
    }

    private string Build(string method, Dictionary<string, string?> extra)
    {
        var pairs = new List<string>
        {
            $"method={Uri.EscapeDataString(method)}",
            $"api_key={Uri.EscapeDataString(ApiKey)}",
            "format=json"
        };
        foreach (var (key, value) in extra)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            }
        }

        return "https://ws.audioscrobbler.com/2.0/?" + string.Join("&", pairs);
    }

    private static void ThrowIfError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            var message = root.GetPropertyString("message") ?? error.ToString();
            throw new JsonApiException("Last.fm", message);
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
