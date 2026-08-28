using GalaxyMusicDataset.Data;

namespace GalaxyMusicDataset.Data.Entities;

public sealed class TrackTag
{
    public long Id { get; set; }
    public long TrackId { get; set; }
    public long TagId { get; set; }
    public EnrichmentSource Source { get; set; }
    public int Weight { get; set; }

    public Track Track { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
