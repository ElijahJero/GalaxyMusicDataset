using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;

namespace GalaxyMusicDataset.Services.VocaDb;

public static class VocaDbFamily
{
    public static bool IsFamily(EnrichmentSource source) =>
        source is EnrichmentSource.VocaDb or EnrichmentSource.UtaiteDb or EnrichmentSource.TouhouDb;

    public static string DisplayName(EnrichmentSource source) => source switch
    {
        EnrichmentSource.LastFm => "Last.fm",
        EnrichmentSource.MusicBrainz => "MusicBrainz",
        EnrichmentSource.Discogs => "Discogs",
        EnrichmentSource.TheAudioDb => "TheAudioDB",
        EnrichmentSource.VocaDb => "VocaDB",
        EnrichmentSource.UtaiteDb => "UtaiteDB",
        EnrichmentSource.TouhouDb => "TouhouDB",
        _ => source.ToString()
    };

    public static string SongPageUrl(EnrichmentSource source, string id) => source switch
    {
        EnrichmentSource.VocaDb => $"https://vocadb.net/S/{id}",
        EnrichmentSource.UtaiteDb => $"https://utaitedb.net/S/{id}",
        EnrichmentSource.TouhouDb => $"https://touhoudb.com/S/{id}",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not a VocaDB-family source.")
    };

    public static string? GetSongId(Track track, EnrichmentSource source) => source switch
    {
        EnrichmentSource.VocaDb => track.VocaDbSongId,
        EnrichmentSource.UtaiteDb => track.UtaiteDbSongId,
        EnrichmentSource.TouhouDb => track.TouhouDbSongId,
        _ => null
    };

    public static void SetSongId(Track track, EnrichmentSource source, string? id)
    {
        id = CatalogService.Coalesce(GetSongId(track, source), id);
        switch (source)
        {
            case EnrichmentSource.VocaDb:
                track.VocaDbSongId = id;
                break;
            case EnrichmentSource.UtaiteDb:
                track.UtaiteDbSongId = id;
                break;
            case EnrichmentSource.TouhouDb:
                track.TouhouDbSongId = id;
                break;
        }
    }

    public static string AliasSource(EnrichmentSource source) => DisplayName(source);
}
