namespace GalaxyMusicDataset.Data.Entities;

public sealed class SyncState
{
    public int Id { get; set; } = 1;
    public long? NewestUnix { get; set; }
    public long? OldestUnix { get; set; }
    public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public string? LastSyncError { get; set; }
    public bool IsBackfillComplete { get; set; }
    public DateTimeOffset? BackfillCursorDay { get; set; }
    public DateTimeOffset? AccountRegisteredUtc { get; set; }
    public long? LastFmPlaycount { get; set; }
    public string? LastFmUsername { get; set; }
    public bool EnrichmentPaused { get; set; }
    public int IncrementalRuns { get; set; }
    public int BackfillDaysCompleted { get; set; }
}
