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
}
