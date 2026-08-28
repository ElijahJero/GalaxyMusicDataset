using GalaxyMusicDataset.Data;

namespace GalaxyMusicDataset.Data.Entities;

public sealed class TrackLookup
{
    public long Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public long? TrackId { get; set; }
    public string ArtistName { get; set; } = "";
    public string TrackName { get; set; } = "";
    public string? AlbumName { get; set; }
    public LookupStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? CandidateJson { get; set; }
    public string? MatchedMbid { get; set; }
    public double? BestScore { get; set; }
    public string? ErrorMessage { get; set; }
    public string? QueryUsed { get; set; }

    public Track? Track { get; set; }
}
