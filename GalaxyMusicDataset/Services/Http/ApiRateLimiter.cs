namespace GalaxyMusicDataset.Services.Http;

public sealed class ApiRateLimiter(TimeSpan minInterval)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextAllowed)
            {
                await Task.Delay(_nextAllowed - now, cancellationToken);
            }

            _nextAllowed = DateTimeOffset.UtcNow + minInterval;
        }
        finally
        {
            _gate.Release();
        }
    }
}
