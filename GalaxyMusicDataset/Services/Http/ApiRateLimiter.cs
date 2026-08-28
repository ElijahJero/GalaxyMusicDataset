namespace GalaxyMusicDataset.Services.Http;

public sealed class ApiRateLimiter(TimeSpan minInterval)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                TimeSpan wait;
                lock (_lock)
                {
                    wait = _nextAllowed - DateTimeOffset.UtcNow;
                    if (wait <= TimeSpan.Zero)
                    {
                        _nextAllowed = DateTimeOffset.UtcNow + minInterval;
                        return;
                    }
                }

                await Task.Delay(wait, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Push the next allowed call later. Used when MusicBrainz returns 503 "busy".
    /// </summary>
    public void Postpone(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var until = DateTimeOffset.UtcNow + delay;
        lock (_lock)
        {
            if (until > _nextAllowed)
            {
                _nextAllowed = until;
            }
        }
    }
}
