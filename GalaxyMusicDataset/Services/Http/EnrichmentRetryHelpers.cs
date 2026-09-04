namespace GalaxyMusicDataset.Services.Http;

public static class EnrichmentRetryHelpers
{
    public static readonly TimeSpan DefaultErrorCooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TransientErrorCooldown = TimeSpan.FromMinutes(30);

    public static bool IsTransientFailure(Exception ex)
    {
        if (ex is not JsonApiException api)
        {
            return false;
        }

        return HttpResponseHelpers.IsTransientStatus(api.StatusCode)
               || IsTransientFailureMessage(api.Message);
    }

    public static bool IsTransientFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("busy", StringComparison.OrdinalIgnoreCase)
               || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Gave up after", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 503", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 502", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Service Temporarily Unavailable", StringComparison.OrdinalIgnoreCase);
    }

    public static TimeSpan ErrorRetryCooldown(string? errorMessage) =>
        IsTransientFailureMessage(errorMessage) ? TransientErrorCooldown : DefaultErrorCooldown;

    public static string BusyMessage(string source, int? statusCode) =>
        statusCode is null
            ? $"{source} busy (request timed out); will retry after cooldown."
            : $"{source} busy (HTTP {statusCode}); will retry after cooldown.";
}
