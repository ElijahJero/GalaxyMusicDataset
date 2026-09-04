using System.Net;

namespace GalaxyMusicDataset.Services.Http;

public sealed class JsonApiException(string source, string message, int? statusCode = null) : Exception(message)
{
    public string SourceName { get; } = source;
    public int? StatusCode { get; } = statusCode;
}

public static class HttpResponseHelpers
{
    public static async Task<string> ReadSuccessBodyAsync(
        HttpResponseMessage response,
        string source,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        var snippet = body.Length > 400 ? body[..400] : body;
        throw new JsonApiException(
            source,
            $"{source} HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}",
            (int)response.StatusCode);
    }

    public static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta + TimeSpan.FromMilliseconds(50);
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        var seconds = response.StatusCode == HttpStatusCode.TooManyRequests
            ? Math.Min(30, Math.Pow(2, attempt))
            : Math.Min(20, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Backoff for MusicBrainz 503/502, matching python-musicbrainzngs
    /// (retry_num * 2s, up to 8 tries) plus a longer floor so a 1-second
    /// Retry-After header cannot hammer the server.
    /// </summary>
    public static TimeSpan BusyBackoff(HttpResponseMessage? response, int attempt)
    {
        var ngsStyle = TimeSpan.FromSeconds(Math.Max(1, attempt) * 2.0);
        var floor = TimeSpan.FromSeconds(Math.Min(60, Math.Max(1, attempt) * 5.0));
        var header = response is null ? TimeSpan.Zero : RetryDelay(response, attempt);
        var wait = ngsStyle;
        if (header > wait)
        {
            wait = header;
        }

        if (floor > wait)
        {
            wait = floor;
        }

        return wait;
    }

    public static bool IsTransientStatus(int? statusCode) =>
        statusCode is 429 or 500 or 502 or 503;

    /// <summary>
    /// HttpClient.Timeout cancels via TaskCanceledException. That must not be
    /// treated as host shutdown cancellation, or enrichment never records the
    /// failure and retries the same track forever.
    /// </summary>
    public static bool IsHttpClientTimeout(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }

            if (current is TaskCanceledException)
            {
                return true;
            }

            if (current.Message.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
