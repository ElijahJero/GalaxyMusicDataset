using GalaxyMusicDataset.Data;

namespace GalaxyMusicDataset.Data.Entities;

public sealed class TrackSourcePayload
{
    public long Id { get; set; }
    public long TrackId { get; set; }
    public EnrichmentSource Source { get; set; }
    public SourceFetchStatus Status { get; set; }
    public string? ExternalId { get; set; }
    public string? PayloadJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }

    public Track Track { get; set; } = null!;
}
