namespace GalaxyMusicDataset.Data.Entities;

public sealed class Album
{
    public long Id { get; set; }
    public long? ArtistId { get; set; }
    public string Title { get; set; } = "";
    public string? Mbid { get; set; }
    public int? ReleaseYear { get; set; }
    public string? CoverUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Artist? Artist { get; set; }
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}
