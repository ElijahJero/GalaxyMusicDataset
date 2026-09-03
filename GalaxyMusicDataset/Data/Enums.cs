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
    TouhouDb = 6
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
    TouhouDb = 9
}

public enum JobStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    Partial = 4
}
