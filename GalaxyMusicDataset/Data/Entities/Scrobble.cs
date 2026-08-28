namespace GalaxyMusicDataset.Data.Entities;

public sealed class Scrobble
{
    public long Id { get; set; }
    public long TrackId { get; set; }
    public DateTimeOffset PlayedAt { get; set; }
    public long UnixTimestamp { get; set; }
    public string OriginalArtist { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public string? OriginalAlbum { get; set; }
    public string? LastFmTrackMbid { get; set; }
    public string? LastFmArtistMbid { get; set; }
    public string? LastFmAlbumMbid { get; set; }

    public Track Track { get; set; } = null!;
}
