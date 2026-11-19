namespace GalaxyMusicDataset.Data;

public enum LookupStatus
{
    Pending = 0,
    InProgress = 1,
    AutoMatched = 2,
    NeedsReview = 3,
    NotFound = 4,
    Failed = 5,
    Rejected = 6,
    ManualMatched = 7
}

public enum EnrichmentSource
{
    LastFm = 0,
    MusicBrainz = 1,
    Discogs = 2,
    TheAudioDb = 3,
    VocaDb = 4,
    UtaiteDb = 5,
    TouhouDb = 6,
    Vgmdb = 7,
    Maloja = 8
}

/// <summary>
/// Where a scrobble row was ingested from. Used for multi-provider sync and
/// reconcile deletes that must not erase history imported from another service.
/// </summary>
public enum ScrobbleProvider
{
    LastFm = 0,
    Maloja = 1,
    ListenBrainz = 2,
    LibreFm = 3
}

public enum SourceFetchStatus
{
    NotStarted = 0,
    Success = 1,
    NotFound = 2,
    Error = 3,
    Skipped = 4
}

public enum JobKind
{
    LastFmIncremental = 0,
    LastFmBackfill = 1,
    MusicBrainzLookup = 2,
    LastFmTrackInfo = 3,
    Discogs = 4,
    TheAudioDb = 5,
    SeedSample = 6,
    VocaDb = 7,
    UtaiteDb = 8,
    TouhouDb = 9,
    Vgmdb = 10,
    LastFmReconcile = 11,
    ScrobbleIncremental = 12,
    ScrobbleBackfill = 13,
    ScrobbleReconcile = 14
}

public enum JobStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    Partial = 4
}

public enum AudioLabelKind
{
    Genre = 0,
    Theme = 1,
    Instrument = 2
}
