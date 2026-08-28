using System.Collections.Concurrent;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class AggregationProgress
{
    private readonly ConcurrentQueue<string> _log = new();
    private readonly object _gate = new();

    public string Phase { get; private set; } = "Idle";
    public string? CurrentItem { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastUpdatedUtc { get; private set; }
    public bool SyncRunning { get; private set; }
    public bool EnrichmentRunning { get; private set; }
    public bool EnrichmentPaused { get; private set; }
    public string? BackfillDay { get; private set; }
    public int BackfillDaysCompleted { get; private set; }
    public int CurrentJobProcessed { get; private set; }
    public int CurrentJobSucceeded { get; private set; }
    public int CurrentJobFailed { get; private set; }

    public IReadOnlyList<string> RecentLog
    {
        get
        {
            var items = _log.ToArray();
            Array.Reverse(items);
            return items;
        }
    }

    public void SetPhase(string phase, string? currentItem = null)
    {
        lock (_gate)
        {
            Phase = phase;
            if (currentItem is not null)
            {
                CurrentItem = currentItem;
            }

            LastUpdatedUtc = DateTimeOffset.UtcNow;
        }
    }

    public void SetCurrentItem(string? item)
    {
        CurrentItem = item;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    public void SetSyncRunning(bool running) => SyncRunning = running;

    public void SetEnrichmentRunning(bool running) => EnrichmentRunning = running;

    public void SetEnrichmentPaused(bool paused) => EnrichmentPaused = paused;

    public void SetBackfill(string? day, int daysCompleted)
    {
        BackfillDay = day;
        BackfillDaysCompleted = daysCompleted;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    public void SetJobCounts(int processed, int succeeded, int failed)
    {
        CurrentJobProcessed = processed;
        CurrentJobSucceeded = succeeded;
        CurrentJobFailed = failed;
    }

    public void Error(string message)
    {
        LastError = message;
        Log("ERROR " + message);
    }

    public void Log(string message)
    {
        var line = $"{DateTimeOffset.UtcNow:HH:mm:ss} {message}";
        _log.Enqueue(line);
        while (_log.Count > 80 && _log.TryDequeue(out _))
        {
        }

        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    public void ClearError() => LastError = null;
}
