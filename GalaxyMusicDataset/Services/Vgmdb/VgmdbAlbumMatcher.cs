using GalaxyMusicDataset.Services.Normalization;
using GalaxyMusicDataset.Services.VocaDb;

namespace GalaxyMusicDataset.Services.Vgmdb;

public static class VgmdbAlbumMatcher
{
    public const double TitleAutoThreshold = 0.85;
    public const double StrongTitleThreshold = 0.90;
    public const double GenericArtistThreshold = 0.70;
    public const double GenericTitleFloor = 0.55;

    private static readonly HashSet<string> GenericAlbumTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ost",
        "o.s.t.",
        "o.s.t",
        "soundtrack",
        "original soundtrack",
        "original score",
        "game music",
        "complete soundtrack",
        "arranged soundtrack",
        "official soundtrack"
    };

    public static string? SearchQuery(string artist, string title, string? albumTitle)
    {
        if (!string.IsNullOrWhiteSpace(albumTitle) && !IsGenericAlbumTitle(albumTitle))
        {
            return VocaDbClient.SanitizeSearchTerm(albumTitle);
        }

        return VocaDbClient.SanitizeSearchTerm(title)
               ?? VocaDbClient.SanitizeSearchTerm(artist);
    }

    public static bool IsGenericAlbumTitle(string? albumTitle)
    {
        var normalized = TextNormalizer.Normalize(albumTitle);
        return string.IsNullOrWhiteSpace(normalized) || GenericAlbumTitles.Contains(normalized);
    }

    public static VgmdbAlbumHit? PickBestAlbum(
        string artist,
        string title,
        string? albumTitle,
        IReadOnlyList<VgmdbAlbumHit> hits)
    {
        VgmdbAlbumHit? best = null;
        var bestScore = 0d;
        var genericAlbum = IsGenericAlbumTitle(albumTitle);
        foreach (var hit in hits)
        {
            var vsAlbum = string.IsNullOrWhiteSpace(albumTitle) ? 0d : MaxName(albumTitle, hit.Titles);
            var vsTrack = MaxName(title, hit.Titles);
            var vsArtist = MaxName(artist, hit.Titles);
            var titleScore = Math.Max(vsAlbum, vsTrack);
            var accepted = genericAlbum
                ? titleScore >= TitleAutoThreshold
                  || (vsArtist >= GenericArtistThreshold && titleScore >= GenericTitleFloor)
                : titleScore >= TitleAutoThreshold;
            if (!accepted)
            {
                continue;
            }

            var combined = genericAlbum
                ? (0.55 * titleScore) + (0.45 * vsArtist)
                : titleScore;
            if (best is not null && combined <= bestScore)
            {
                continue;
            }

            best = hit;
            bestScore = combined;
        }

        return best;
    }

    public static VgmdbDiscTrack? PickBestTrack(string title, VgmdbAlbum album)
    {
        VgmdbDiscTrack? best = null;
        var bestScore = 0d;
        foreach (var disc in album.Discs)
        {
            foreach (var track in disc.Tracks)
            {
                var score = MaxName(title, track.Names);
                if (best is not null && score <= bestScore)
                {
                    continue;
                }

                best = track;
                bestScore = score;
            }
        }

        if (best is null)
        {
            return null;
        }

        return bestScore >= TitleAutoThreshold ? best : null;
    }

    public static double MaxName(string query, IReadOnlyList<string> names)
    {
        var normalizedQuery = TextNormalizer.Normalize(query);
        var best = 0d;
        foreach (var name in names)
        {
            best = Math.Max(best, StringSimilarity.Ratio(normalizedQuery, TextNormalizer.Normalize(name)));
            var romanized = TextNormalizer.RomanizeIfKana(name);
            if (romanized is not null)
            {
                best = Math.Max(best, StringSimilarity.Ratio(normalizedQuery, romanized));
            }
        }

        return best;
    }
}
