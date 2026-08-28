namespace GalaxyMusicDataset.Data.Entities;

public sealed class ArtistAlias
{
    public long Id { get; set; }
    public long ArtistId { get; set; }
    public string Name { get; set; } = "";
    public string? Locale { get; set; }
    public string Source { get; set; } = "MusicBrainz";
    public bool IsPrimary { get; set; }

    public Artist Artist { get; set; } = null!;
}
