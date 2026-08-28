namespace GalaxyMusicDataset.Data.Entities;

public sealed class Track
{
    public long Id { get; set; }
    public long ArtistId { get; set; }
    public long? AlbumId { get; set; }
    public string Title { get; set; } = "";
    public string? Mbid { get; set; }
    public int? DurationMs { get; set; }
    public string? Isrc { get; set; }
    public string? Summary { get; set; }
    public string? MusicVideoUrl { get; set; }
    public string? DiscogsReleaseId { get; set; }
    public string? TheAudioDbTrackId { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Artist Artist { get; set; } = null!;
    public Album? Album { get; set; }
    public ICollection<Scrobble> Scrobbles { get; set; } = new List<Scrobble>();
    public ICollection<TrackTag> Tags { get; set; } = new List<TrackTag>();
    public ICollection<TrackSourcePayload> SourcePayloads { get; set; } = new List<TrackSourcePayload>();
}
