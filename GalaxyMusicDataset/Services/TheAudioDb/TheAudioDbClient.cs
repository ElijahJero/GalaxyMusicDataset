using System.Text.Json;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.TheAudioDb;

public sealed record AudioDbTrackHit(
    string? Id,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumId,
    int? DurationMs,
    string? Genre,
    string? Mood,
    string? Style,
    string? Theme,
    string? Description,
    string? ThumbUrl,
    string? MusicVideoUrl,
    string? MusicBrainzId,
    string RawJson);

public sealed class TheAudioDbClient(HttpClient http, ApiCallRecorder recorder)
{
    public static readonly ApiRateLimiter RateLimiter = new(TimeSpan.FromMilliseconds(800));

    public required string ApiKey { get; init; }

    public async Task<(AudioDbTrackHit? Best, string RawJson)?> SearchTrackAsync(
        string artist,
        string track,
        CancellationToken cancellationToken)
    {
        var url = $"https://www.theaudiodb.com/api/v1/json/{Uri.EscapeDataString(ApiKey)}/searchtrack.php?s={Uri.EscapeDataString(artist)}&t={Uri.EscapeDataString(track)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var json = await recorder.SendAsync(http, request, "TheAudioDB", RateLimiter, cancellationToken);
        return ParseSearch(json);
    }

    public async Task<string?> LookupAlbumThumbAsync(string albumId, CancellationToken cancellationToken)
    {
        var url = $"https://www.theaudiodb.com/api/v1/json/{Uri.EscapeDataString(ApiKey)}/album.php?m={Uri.EscapeDataString(albumId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var json = await recorder.SendAsync(http, request, "TheAudioDB", RateLimiter, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("album", out var albums) || albums.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var first = albums.ValueKind == JsonValueKind.Array
            ? albums.GetArrayLength() > 0 ? albums[0] : default
            : albums;
        if (first.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return EmptyToNull(first.GetPropertyString("strAlbumThumb"));
    }

    public static (AudioDbTrackHit? Best, string RawJson) ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("track", out var tracks) || tracks.ValueKind == JsonValueKind.Null)
        {
            return (null, json);
        }

        JsonElement first;
        if (tracks.ValueKind == JsonValueKind.Array)
        {
            if (tracks.GetArrayLength() == 0)
            {
                return (null, json);
            }

            first = tracks[0];
        }
        else if (tracks.ValueKind == JsonValueKind.Object)
        {
            first = tracks;
        }
        else
        {
            return (null, json);
        }

        return (ParseTrack(first, json), json);
    }

    public static AudioDbTrackHit ParseTrack(JsonElement first, string rawJson)
    {
        var duration = first.GetPropertyInt("intDuration");
        return new AudioDbTrackHit(
            first.GetPropertyString("idTrack"),
            first.GetPropertyString("strTrack"),
            first.GetPropertyString("strArtist"),
            first.GetPropertyString("strAlbum"),
            first.GetPropertyString("idAlbum"),
            duration,
            EmptyToNull(first.GetPropertyString("strGenre")),
            EmptyToNull(first.GetPropertyString("strMood")),
            EmptyToNull(first.GetPropertyString("strStyle")),
            EmptyToNull(first.GetPropertyString("strTheme")),
            EmptyToNull(first.GetPropertyString("strDescriptionEN"))
                ?? EmptyToNull(first.GetPropertyString("strDescription")),
            EmptyToNull(first.GetPropertyString("strTrackThumb")),
            EmptyToNull(first.GetPropertyString("strMusicVid")),
            EmptyToNull(first.GetPropertyString("strMusicBrainzID")),
            first.GetRawText());
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
