using System.Net.Http.Headers;
using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.Normalization;

namespace GalaxyMusicDataset.Services.MusicBrainz;

public sealed class MusicBrainzClient(HttpClient http, ApiCallRecorder recorder)
{
    /// <summary>
    /// Shared throttle for the MusicBrainz Web Service. Interval is 1.2s on the
    /// public API and much smaller when pointed at a self-hosted mirror.
    /// </summary>
    public static readonly ApiRateLimiter RateLimiter = new(TimeSpan.FromMilliseconds(MusicBrainzOptions.PublicMinIntervalMs));

    /// <summary>
    /// Cover Art Archive is a separate host. Public CAA stays at 1.2s even when
    /// the MusicBrainz WS is local.
    /// </summary>
    public static readonly ApiRateLimiter CoverArtRateLimiter = new(TimeSpan.FromMilliseconds(MusicBrainzOptions.PublicMinIntervalMs));

    public required string UserAgent { get; init; }
    public required MusicBrainzOptions Options { get; init; }

    public async Task<IReadOnlyList<RecordingCandidate>> SearchRecordingsAsync(
        string artist,
        string title,
        string? album,
        CancellationToken cancellationToken)
    {
        var seen = new Dictionary<string, RecordingCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in BuildQueries(artist, title, album))
        {
            var url = Options.RecordingSearchUrl(query);
            var json = await GetWsAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("recordings", out var recordings))
            {
                continue;
            }

            foreach (var rec in recordings.EnumerateArray())
            {
                var candidate = ParseRecording(rec);
                if (candidate is null)
                {
                    continue;
                }

                var scored = candidate with
                {
                    Score = RecordingMatchScorer.Score(artist, title, album, candidate)
                };
                if (!seen.TryGetValue(scored.Mbid, out var existing) || scored.Score > existing.Score)
                {
                    seen[scored.Mbid] = scored;
                }
            }

            // One successful search is enough. Extra album/romaji queries
            // doubled traffic and were a major source of 503 storms.
            if (seen.Count > 0)
            {
                break;
            }
        }

        return seen.Values.OrderByDescending(x => x.Score).Take(8).ToList();
    }

    public async Task<string> GetRecordingJsonAsync(string mbid, CancellationToken cancellationToken)
    {
        var url = Options.RecordingLookupUrl(mbid);
        return await GetWsAsync(url, cancellationToken);
    }

    public async Task<JsonDocument> GetRecordingAsync(string mbid, CancellationToken cancellationToken)
    {
        var json = await GetRecordingJsonAsync(mbid, cancellationToken);
        return JsonDocument.Parse(json);
    }

    public async Task<string?> GetReleaseFrontCoverUrlAsync(string releaseMbid, CancellationToken cancellationToken)
    {
        var url = Options.ReleaseCoverArtUrl(releaseMbid);
        try
        {
            var json = await GetCoverArtAsync(url, cancellationToken);
            return ParseCoverArtFrontUrl(json);
        }
        catch (JsonApiException ex) when (ex.StatusCode is 404)
        {
            return null;
        }
    }

    public async Task<JsonDocument> GetArtistAsync(string mbid, CancellationToken cancellationToken)
    {
        var url = Options.ArtistLookupUrl(mbid);
        var json = await GetWsAsync(url, cancellationToken);
        return JsonDocument.Parse(json);
    }

    public static RecordingCandidate? ParseRecording(JsonElement rec)
    {
        var mbid = rec.GetPropertyString("id");
        var title = rec.GetPropertyString("title");
        if (string.IsNullOrWhiteSpace(mbid) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string artist = "";
        string? artistMbid = null;
        if (rec.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var credit in credits.EnumerateArray())
            {
                var name = credit.GetPropertyString("name") ?? credit.GetPropertyString("artist");
                if (credit.TryGetProperty("artist", out var artistObj))
                {
                    name ??= artistObj.GetPropertyString("name");
                    artistMbid ??= artistObj.GetPropertyString("id");
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            artist = string.Join(" ", names);
        }

        string? album = null;
        string? releaseMbid = null;
        if (rec.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array && releases.GetArrayLength() > 0)
        {
            var first = releases[0];
            album = first.GetPropertyString("title");
            releaseMbid = first.GetPropertyString("id");
        }

        int? lengthMs = rec.GetPropertyInt("length");
        return new RecordingCandidate(
            mbid,
            title,
            artist,
            album,
            lengthMs,
            artistMbid,
            releaseMbid,
            rec.GetPropertyString("disambiguation"),
            0);
    }

    public static MusicBrainzRecordingDetails? ParseRecordingDetails(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var rec = doc.RootElement;
        var mbid = rec.GetPropertyString("id");
        var title = rec.GetPropertyString("title");
        if (string.IsNullOrWhiteSpace(mbid) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string? firstIsrc = null;
        if (rec.TryGetProperty("isrcs", out var isrcs) && isrcs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in isrcs.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    firstIsrc = value;
                    break;
                }
            }
        }

        string? album = null;
        string? releaseMbid = null;
        int? year = null;
        if (rec.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array && releases.GetArrayLength() > 0)
        {
            var first = releases[0];
            album = first.GetPropertyString("title");
            releaseMbid = first.GetPropertyString("id");
            var date = first.GetPropertyString("date");
            if (date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), out var parsedYear))
            {
                year = parsedYear;
            }
        }

        var tags = new List<(string Name, int Count)>();
        if (rec.TryGetProperty("tags", out var tagEl) && tagEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagEl.EnumerateArray())
            {
                var name = tag.GetPropertyString("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    tags.Add((name, tag.GetPropertyInt("count") ?? 0));
                }
            }
        }

        var genres = new List<string>();
        if (rec.TryGetProperty("genres", out var genreEl) && genreEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genreEl.EnumerateArray())
            {
                var name = genre.GetPropertyString("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    genres.Add(name);
                }
            }
        }

        return new MusicBrainzRecordingDetails(
            mbid,
            title,
            rec.GetPropertyInt("length"),
            firstIsrc,
            releaseMbid,
            album,
            year,
            tags,
            genres,
            json);
    }

    public static bool HasRecordingDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("id", out _)
                   && (root.TryGetProperty("isrcs", out _) || root.TryGetProperty("genres", out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? ParseCoverArtFrontUrl(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallback = null;
        foreach (var image in images.EnumerateArray())
        {
            var thumb = image.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Object
                ? thumbs.GetPropertyString("small")
                  ?? thumbs.GetPropertyString("250")
                  ?? thumbs.GetPropertyString("large")
                : null;
            var uri = thumb ?? image.GetPropertyString("image");
            if (string.IsNullOrWhiteSpace(uri))
            {
                continue;
            }

            fallback ??= uri;
            if (image.TryGetProperty("front", out var front) && front.ValueKind is JsonValueKind.True)
            {
                return uri;
            }
        }

        return fallback;
    }

    private static IEnumerable<string> BuildQueries(string artist, string title, string? album)
    {
        var titleQ = EscapeLucene(title);
        var artistQ = EscapeLucene(artist);
        if (!string.IsNullOrWhiteSpace(album))
        {
            yield return $"recording:\"{titleQ}\" AND artist:\"{artistQ}\" AND release:\"{EscapeLucene(album)}\"";
        }

        yield return $"recording:\"{titleQ}\" AND artist:\"{artistQ}\"";

        var romanTitle = TextNormalizer.RomanizeIfKana(title);
        var romanArtist = TextNormalizer.RomanizeIfKana(artist);
        if (romanTitle is not null || romanArtist is not null)
        {
            var t = romanTitle ?? titleQ;
            var a = romanArtist ?? artistQ;
            yield return $"recording:\"{EscapeLucene(t)}\" AND artist:\"{EscapeLucene(a)}\"";
        }
    }

    private static string EscapeLucene(string value)
    {
        var normalized = value.Replace("\"", " ", StringComparison.Ordinal);
        return normalized.Trim();
    }

    private Task<string> GetWsAsync(string url, CancellationToken cancellationToken) =>
        SendAsync(url, "MusicBrainz", RateLimiter, cancellationToken);

    private Task<string> GetCoverArtAsync(string url, CancellationToken cancellationToken) =>
        SendAsync(url, "CoverArtArchive", CoverArtRateLimiter, cancellationToken);

    private async Task<string> SendAsync(string url, string source, ApiRateLimiter limiter, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await recorder.SendAsync(http, request, source, limiter, cancellationToken, maxAttempts: 8);
    }
}
