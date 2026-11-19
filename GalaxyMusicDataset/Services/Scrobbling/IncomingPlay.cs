using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.LastFm;

namespace GalaxyMusicDataset.Services.Scrobbling;

/// <summary>
/// Provider-agnostic listen used by ingest. Timestamps are Unix seconds UTC.
/// </summary>
public sealed record IncomingPlay(
    ScrobbleProvider Source,
    string ArtistName,
    string TrackName,
    string? AlbumName,
    long? TimestampUnix,
    string? TrackMbid,
    string? ArtistMbid,
    string? AlbumMbid,
    bool IsNowPlaying,
    string RawJson);

public sealed record ScrobbleUserInfo(string Name, long Playcount, DateTimeOffset? RegisteredUtc);

public sealed record PlayWindowResult(
    IReadOnlyList<IncomingPlay> Plays,
    int ReportedTotal,
    int PageCount,
    bool Ok,
    string? Warning);

public static class IncomingPlayMapper
{
    public static IncomingPlay FromLastFm(LastFmRecentTrack track, ScrobbleProvider source = ScrobbleProvider.LastFm) =>
        new(
            source,
            track.ArtistName,
            track.TrackName,
            track.AlbumName,
            track.TimestampUnix,
            track.TrackMbid,
            track.ArtistMbid,
            track.AlbumMbid,
            track.IsNowPlaying,
            track.RawJson);

    public static IEnumerable<IncomingPlay> FromLastFm(
        IEnumerable<LastFmRecentTrack> tracks,
        ScrobbleProvider source = ScrobbleProvider.LastFm) =>
        tracks.Select(t => FromLastFm(t, source));
}
