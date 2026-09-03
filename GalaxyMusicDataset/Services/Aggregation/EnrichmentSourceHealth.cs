using System.Collections.Concurrent;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.Aggregation;

/// <summary>
/// Pauses a single enrichment source after repeated transient API failures so
/// other enrichers can run instead of hammering a sick endpoint.
/// </summary>
public sealed class EnrichmentSourceHealth
{
    public const int ConsecutiveFailuresBeforePause = 3;
    public static readonly TimeSpan PauseDuration = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<EnrichmentSource, SourceState> _states = new();

    public bool IsPaused(EnrichmentSource source)
    {
        var state = _states.GetOrAdd(source, _ => new SourceState());
        lock (state.Lock)
        {
            ClearExpiredPause(state);
            return state.PausedUntil is { } until && DateTimeOffset.UtcNow < until;
        }
    }

    public DateTimeOffset? PausedUntil(EnrichmentSource source)
    {
        var state = _states.GetOrAdd(source, _ => new SourceState());
        lock (state.Lock)
        {
            ClearExpiredPause(state);
            return state.PausedUntil;
        }
    }

    public void RecordSuccess(EnrichmentSource source)
    {
        var state = _states.GetOrAdd(source, _ => new SourceState());
        lock (state.Lock)
        {
            state.ConsecutiveTransientFailures = 0;
            state.PausedUntil = null;
        }
    }

    /// <returns>True when this failure opened the circuit (source just paused).</returns>
    public bool RecordTransientFailure(EnrichmentSource source, ApiRateLimiter? limiter)
    {
        var state = _states.GetOrAdd(source, _ => new SourceState());
        lock (state.Lock)
        {
            ClearExpiredPause(state);
            state.ConsecutiveTransientFailures++;
            if (state.ConsecutiveTransientFailures >= ConsecutiveFailuresBeforePause)
            {
                var until = DateTimeOffset.UtcNow + PauseDuration;
                var opened = state.PausedUntil is null || until > state.PausedUntil;
                state.PausedUntil = until;
                if (limiter is not null)
                {
                    limiter.Postpone(PauseDuration);
                }

                return opened;
            }

            if (limiter is not null)
            {
                limiter.Postpone(TimeSpan.FromSeconds(15));
            }

            return false;
        }
    }

    private static void ClearExpiredPause(SourceState state)
    {
        if (state.PausedUntil is { } until && DateTimeOffset.UtcNow >= until)
        {
            state.PausedUntil = null;
            state.ConsecutiveTransientFailures = 0;
        }
    }

    private sealed class SourceState
    {
        public object Lock { get; } = new();
        public int ConsecutiveTransientFailures;
        public DateTimeOffset? PausedUntil;
    }
}
