namespace GalaxyMusicDataset.Data.Entities;

public sealed class Artist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? SortName { get; set; }
    public string? Mbid { get; set; }
    public string? LastFmUrl { get; set; }
    public string? ImageUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ArtistAlias> Aliases { get; set; } = new List<ArtistAlias>();
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
    public ICollection<Album> Albums { get; set; } = new List<Album>();
}
