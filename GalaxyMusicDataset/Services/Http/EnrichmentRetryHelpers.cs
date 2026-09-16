using System.Net.Http;
using System.Net.Sockets;

namespace GalaxyMusicDataset.Services.Http;

public static class EnrichmentRetryHelpers
{
    public static readonly TimeSpan DefaultErrorCooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TransientErrorCooldown = TimeSpan.FromMinutes(30);

    public static bool IsTransientFailure(Exception ex)
    {
        if (IsUnreachableFailure(ex))
        {
            return true;
        }

        if (ex is JsonApiException api)
        {
            return HttpResponseHelpers.IsTransientStatus(api.StatusCode)
                   || IsTransientFailureMessage(api.Message);
        }

        return IsTransientFailureMessage(ex.Message);
    }

    /// <summary>
    /// DNS/TCP/TLS failures that will not recover by trying the next track.
    /// </summary>
    public static bool IsUnreachableFailure(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http
                && http.HttpRequestError is HttpRequestError.ConnectionError
                    or HttpRequestError.NameResolutionError
                    or HttpRequestError.SecureConnectionError)
            {
                return true;
            }

            if (current is SocketException socket
                && socket.SocketErrorCode is SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable
                    or SocketError.NetworkDown
                    or SocketError.HostNotFound
                    or SocketError.TryAgain
                    or SocketError.ConnectionRefused
                    or SocketError.TimedOut
                    or SocketError.ConnectionReset)
            {
                return true;
            }

            if (IsUnreachableFailureMessage(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUnreachableFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("No route to host", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Connection reset", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("nodename nor servname provided", StringComparison.OrdinalIgnoreCase)
               || message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("SSL_ERROR_SYSCALL", StringComparison.OrdinalIgnoreCase)
               || message.Contains("failed to authenticate", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTransientFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return IsUnreachableFailureMessage(message)
               || message.Contains("busy", StringComparison.OrdinalIgnoreCase)
               || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Gave up after", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 503", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 502", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Service Temporarily Unavailable", StringComparison.OrdinalIgnoreCase)
               || message.Contains("is unreachable", StringComparison.OrdinalIgnoreCase);
    }

    public static string UnreachableMessage(string source) =>
        $"{source} is unreachable. The public vgmdb.info proxy is often down — set Proxy URL to a self-hosted hufman/vgmdb instance.";

    public static TimeSpan ErrorRetryCooldown(string? errorMessage) =>
        IsTransientFailureMessage(errorMessage) ? TransientErrorCooldown : DefaultErrorCooldown;

    public static string BusyMessage(string source, int? statusCode) =>
        statusCode is null
            ? $"{source} busy (request timed out); will retry after cooldown."
            : $"{source} busy (HTTP {statusCode}); will retry after cooldown.";
}
