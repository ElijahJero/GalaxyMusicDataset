namespace GalaxyMusicDataset.Data.Entities;

public sealed class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";

    public ICollection<TrackTag> TrackTags { get; set; } = new List<TrackTag>();
}
