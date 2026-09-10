using GalaxyMusicDataset.Data;

namespace GalaxyMusicDataset.Data.Entities;

public sealed class TrackAudioLabel
{
    public long Id { get; set; }
    public long TrackId { get; set; }
    public AudioLabelKind Kind { get; set; }
    public string Name { get; set; } = "";
    public double? Score { get; set; }

    public TrackAudioProfile Profile { get; set; } = null!;
}
