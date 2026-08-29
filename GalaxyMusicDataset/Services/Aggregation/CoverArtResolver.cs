using System.Text.Json;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Discogs;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.TheAudioDb;

namespace GalaxyMusicDataset.Services.Aggregation;

public static class CoverArtResolver
{
    /// <summary>
    /// Last.fm's default star placeholder. Treat as missing so Discogs / TheAudioDB / CAA can replace it.
    /// </summary>
    public const string LastFmPlaceholderToken = LastFmClient.PlaceholderImageToken;

    public static readonly EnrichmentSource[] FallbackOrder =
    [
        EnrichmentSource.Discogs,
        EnrichmentSource.TheAudioDb,
        EnrichmentSource.LastFm
    ];

    public static bool IsPlaceholder(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        return url.Contains(LastFmPlaceholderToken, StringComparison.OrdinalIgnoreCase);
    }

    public static bool NeedsFallback(string? url) => IsPlaceholder(url);

    public static string? UsableOrNull(string? url) =>
        IsPlaceholder(url) ? null : url!.Trim();

    public static bool TrySetCover(Album? album, string? url)
    {
        if (album is null)
        {
            return false;
        }

        var incoming = UsableOrNull(url);
        if (incoming is null || !NeedsFallback(album.CoverUrl))
        {
            return false;
        }

        album.CoverUrl = incoming;
        album.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public static string? ExtractCoverUrl(EnrichmentSource source, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var raw = source switch
            {
                EnrichmentSource.LastFm => LastFmClient.ParseTrackInfo(json)?.AlbumImageUrl,
                EnrichmentSource.Discogs => CoverFromDiscogs(json),
                EnrichmentSource.TheAudioDb => TheAudioDbClient.ParseSearch(json).Best?.ThumbUrl,
                _ => null
            };
            return UsableOrNull(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? CoverFromPayloads(IEnumerable<(EnrichmentSource Source, string? Json)> payloads)
    {
        var bySource = payloads.ToDictionary(p => p.Source, p => p.Json);
        foreach (var source in FallbackOrder)
        {
            if (!bySource.TryGetValue(source, out var json))
            {
                continue;
            }

            var cover = ExtractCoverUrl(source, json);
            if (cover is not null)
            {
                return cover;
            }
        }

        return null;
    }

    public static string? ReleaseMbidFromMusicBrainzJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return MusicBrainz.MusicBrainzClient.ParseRecordingDetails(json)?.ReleaseMbid;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? CoverFromDiscogs(string json)
    {
        var release = DiscogsClient.ParseRelease(json);
        if (!string.IsNullOrWhiteSpace(release?.CoverUrl))
        {
            return release.CoverUrl;
        }

        return DiscogsClient.ParseSearch(json).Best?.CoverUrl;
    }
}
