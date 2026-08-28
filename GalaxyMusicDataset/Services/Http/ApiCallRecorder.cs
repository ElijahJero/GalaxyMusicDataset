using System.Collections.Concurrent;
using System.Diagnostics;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Http;

public sealed class ApiCallRecorder(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<string, ApiSourceStats> _stats = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ApiSourceStats> Snapshot() =>
        _stats.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.OrdinalIgnoreCase);

    public async Task<string> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        string source,
        ApiRateLimiter limiter,
        CancellationToken cancellationToken,
        int maxAttempts = 4)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await limiter.WaitAsync(cancellationToken);
            var started = Stopwatch.GetTimestamp();
            using var attemptRequest = await CloneAsync(request, cancellationToken);
            HttpResponseMessage? response = null;
            try
            {
                response = await client.SendAsync(attemptRequest, cancellationToken);
                var duration = Stopwatch.GetElapsedTime(started);
                var url = attemptRequest.RequestUri?.ToString() ?? "";
                var status = (int)response.StatusCode;
                if (HttpResponseHelpers.IsTransientStatus(status))
                {
                    var message = $"HTTP {status}";
                    Record(source, status, false, (int)duration.TotalMilliseconds, message);
                    await PersistLogAsync(source, attemptRequest.Method.Method, url, status, false, (int)duration.TotalMilliseconds, message, cancellationToken);

                    var backoff = HttpResponseHelpers.BusyBackoff(response, attempt);
                    limiter.Postpone(backoff);
                    if (attempt == maxAttempts)
                    {
                        throw new JsonApiException(source, $"{source} {message} after {maxAttempts} attempts.", status);
                    }

                    continue;
                }

                var body = await HttpResponseHelpers.ReadSuccessBodyAsync(response, source, cancellationToken);
                Record(source, status, true, (int)duration.TotalMilliseconds, null);
                await PersistLogAsync(source, attemptRequest.Method.Method, url, status, true, (int)duration.TotalMilliseconds, null, cancellationToken);
                return body;
            }
            catch (JsonApiException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                var duration = Stopwatch.GetElapsedTime(started);
                Record(source, response is null ? null : (int)response.StatusCode, false, (int)duration.TotalMilliseconds, ex.Message);
                await PersistLogAsync(source, request.Method.Method, request.RequestUri?.ToString() ?? "", response is null ? null : (int)response.StatusCode, false, (int)duration.TotalMilliseconds, ex.Message, cancellationToken);
                if (attempt == maxAttempts)
                {
                    throw;
                }

                var backoff = HttpResponseHelpers.BusyBackoff(response, attempt);
                limiter.Postpone(backoff);
            }
            finally
            {
                response?.Dispose();
            }
        }

        throw last ?? new InvalidOperationException($"{source} request failed.");
    }

    private void Record(string source, int? status, bool success, int durationMs, string? error)
    {
        var stats = _stats.GetOrAdd(source, _ => new ApiSourceStats(source));
        stats.Record(status, success, durationMs, error);
    }

    private async Task PersistLogAsync(
        string source,
        string method,
        string url,
        int? status,
        bool success,
        int durationMs,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ApiRequestLogs.Add(new ApiRequestLog
            {
                Source = source,
                Method = method,
                Url = TrimUrl(url),
                StatusCode = status,
                Success = success,
                DurationMs = durationMs,
                Error = error is { Length: > 2000 } ? error[..2000] : error,
                At = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);

            var cutoff = await db.ApiRequestLogs
                .OrderByDescending(x => x.Id)
                .Skip(500)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            if (cutoff.Count > 0)
            {
                await db.ApiRequestLogs.Where(x => cutoff.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch
        {
            // Progress logging must never fail the API call.
        }
    }

    private static string TrimUrl(string url)
    {
        if (url.Length <= 2048)
        {
            return url;
        }

        return url[..2048];
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

public sealed class ApiSourceStats(string source)
{
    private readonly object _lock = new();

    public string Source { get; } = source;
    public long TotalRequests { get; private set; }
    public long Successes { get; private set; }
    public long Failures { get; private set; }
    public int? LastStatusCode { get; private set; }
    public DateTimeOffset? LastCallUtc { get; private set; }
    public string? LastError { get; private set; }
    public int LastDurationMs { get; private set; }
    public long TotalDurationMs { get; private set; }

    public void Record(int? status, bool success, int durationMs, string? error)
    {
        lock (_lock)
        {
            TotalRequests++;
            if (success)
            {
                Successes++;
            }
            else
            {
                Failures++;
            }

            LastStatusCode = status;
            LastCallUtc = DateTimeOffset.UtcNow;
            LastError = error;
            LastDurationMs = durationMs;
            TotalDurationMs += durationMs;
        }
    }

    public ApiSourceStats Clone()
    {
        lock (_lock)
        {
            return new ApiSourceStats(Source)
            {
                TotalRequests = TotalRequests,
                Successes = Successes,
                Failures = Failures,
                LastStatusCode = LastStatusCode,
                LastCallUtc = LastCallUtc,
                LastError = LastError,
                LastDurationMs = LastDurationMs,
                TotalDurationMs = TotalDurationMs
            };
        }
    }

    private ApiSourceStats() : this("")
    {
    }
}
