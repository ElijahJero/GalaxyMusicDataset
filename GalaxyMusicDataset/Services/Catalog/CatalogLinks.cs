using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.VocaDb;

namespace GalaxyMusicDataset.Services.Catalog;

public sealed record ExternalLink(string Label, string Url);

public static class CatalogLinks
{
    public static string? MusicBrainzRecording(string? mbid)
    {
        var id = NullIfEmpty(mbid);
        return id is null ? null : $"https://musicbrainz.org/recording/{Uri.EscapeDataString(id)}";
    }

    public static string? MusicBrainzRelease(string? mbid)
    {
        var id = NullIfEmpty(mbid);
        return id is null ? null : $"https://musicbrainz.org/release/{Uri.EscapeDataString(id)}";
    }

    public static string? DiscogsRelease(string? id)
    {
        var releaseId = NullIfEmpty(id);
        return releaseId is null ? null : $"https://www.discogs.com/release/{Uri.EscapeDataString(releaseId)}";
    }

    public static string? VgmdbAlbum(string? id)
    {
        var albumId = NullIfEmpty(id);
        return albumId is null ? null : $"https://vgmdb.net/album/{Uri.EscapeDataString(albumId)}";
    }

    public static string? TheAudioDbTrack(string? id)
    {
        var trackId = NullIfEmpty(id);
        return trackId is null ? null : $"https://www.theaudiodb.com/track/{Uri.EscapeDataString(trackId)}";
    }

    public static string? VocaDbFamilySong(EnrichmentSource source, string? id)
    {
        var songId = NullIfEmpty(id);
        return songId is null ? null : VocaDbFamily.SongPageUrl(source, songId);
    }

    public static string? LastFmTrackFromPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return NullIfEmpty(LastFmClient.ParseTrackInfo(json)?.TrackUrl);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string? LastFmTrackSlug(string artist, string title)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return $"https://www.last.fm/music/{LastFmSegment(artist)}/_/{LastFmSegment(title)}";
    }

    public static IReadOnlyList<ExternalLink> ForTrack(
        string? recordingMbid,
        string? releaseMbid,
        string? discogsReleaseId,
        string? vocaDbSongId,
        string? utaiteDbSongId,
        string? touhouDbSongId,
        string? theAudioDbTrackId,
        string? vgmdbAlbumId,
        string? lastFmPayloadJson,
        string artistName,
        string title)
    {
        var links = new List<ExternalLink>();
        Add(links, "MusicBrainz", MusicBrainzRecording(recordingMbid));
        Add(links, "MusicBrainz release", MusicBrainzRelease(releaseMbid));
        Add(links, "Discogs", DiscogsRelease(discogsReleaseId));
        Add(links, "VGMdb", VgmdbAlbum(vgmdbAlbumId));
        Add(links, "VocaDB", VocaDbFamilySong(EnrichmentSource.VocaDb, vocaDbSongId));
        Add(links, "UtaiteDB", VocaDbFamilySong(EnrichmentSource.UtaiteDb, utaiteDbSongId));
        Add(links, "TouhouDB", VocaDbFamilySong(EnrichmentSource.TouhouDb, touhouDbSongId));
        Add(links, "Last.fm", LastFmUrl(lastFmPayloadJson, artistName, title));
        Add(links, "TheAudioDB", TheAudioDbTrack(theAudioDbTrackId));
        return links;
    }

    private static string? LastFmUrl(string? lastFmPayloadJson, string artistName, string title)
    {
        var fromPayload = LastFmTrackFromPayload(lastFmPayloadJson);
        if (fromPayload is not null)
        {
            return fromPayload;
        }

        return string.IsNullOrWhiteSpace(lastFmPayloadJson)
            ? null
            : LastFmTrackSlug(artistName, title);
    }

    private static void Add(List<ExternalLink> links, string label, string? url)
    {
        if (url is not null)
        {
            links.Add(new ExternalLink(label, url));
        }
    }

    private static string LastFmSegment(string value) =>
        Uri.EscapeDataString(value.Trim()).Replace("%20", "+", StringComparison.Ordinal);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
