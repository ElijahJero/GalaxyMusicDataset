using System.Text.Json;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.TheAudioDb;

public sealed record AudioDbTrackHit(string? Id, string? Title, string? Artist, string? Album, int? DurationMs, string? Genre, string RawJson);

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
        else
        {
            first = tracks;
        }

        var duration = first.GetPropertyInt("intDuration");
        var hit = new AudioDbTrackHit(
            first.GetPropertyString("idTrack"),
            first.GetPropertyString("strTrack"),
            first.GetPropertyString("strArtist"),
            first.GetPropertyString("strAlbum"),
            duration,
            first.GetPropertyString("strGenre"),
            first.GetRawText());
        return (hit, json);
    }
}
