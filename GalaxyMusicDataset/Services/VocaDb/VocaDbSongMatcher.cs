using GalaxyMusicDataset.Services.Normalization;

namespace GalaxyMusicDataset.Services.VocaDb;

public static class VocaDbSongMatcher
{
    public const double TitleAutoThreshold = 0.85;
    public const double ArtistAutoThreshold = 0.70;
    public const double StrongTitleThreshold = 0.90;
    public const double VocalistArtistThreshold = 0.55;

    public static VocaDbSongHit? PickBest(string queryArtist, string queryTitle, IReadOnlyList<VocaDbSongHit> items)
    {
        VocaDbSongHit? best = null;
        var bestCombined = 0d;
        var bestTitle = 0d;
        var bestArtist = 0d;
        foreach (var item in items)
        {
            var title = MaxTitle(queryTitle, item);
            var artist = MaxArtist(queryArtist, item);
            var combined = (0.55 * title) + (0.45 * artist);
            if (string.Equals(item.SongType, "Original", StringComparison.OrdinalIgnoreCase))
            {
                combined = Math.Min(1, combined + 0.03);
            }

            if (best is not null && combined <= bestCombined)
            {
                continue;
            }

            best = item;
            bestCombined = combined;
            bestTitle = title;
            bestArtist = artist;
        }

        if (best is null)
        {
            return null;
        }

        if (bestTitle >= TitleAutoThreshold && bestArtist >= ArtistAutoThreshold)
        {
            return best;
        }

        if (bestTitle >= StrongTitleThreshold && bestArtist >= VocalistArtistThreshold)
        {
            return best;
        }

        return null;
    }

    public static double MaxTitle(string queryTitle, VocaDbSongHit item)
    {
        var query = TextNormalizer.Normalize(queryTitle);
        var best = 0d;
        foreach (var name in item.AllTitles)
        {
            best = Math.Max(best, StringSimilarity.Ratio(query, TextNormalizer.Normalize(name)));
            var romanized = TextNormalizer.RomanizeIfKana(name);
            if (romanized is not null)
            {
                best = Math.Max(best, StringSimilarity.Ratio(query, romanized));
            }
        }

        return best;
    }

    public static double MaxArtist(string queryArtist, VocaDbSongHit item)
    {
        var query = TextNormalizer.Normalize(queryArtist);
        var best = StringSimilarity.Ratio(query, TextNormalizer.Normalize(item.ArtistString));
        foreach (var credit in item.Artists)
        {
            foreach (var name in credit.AllNames)
            {
                best = Math.Max(best, StringSimilarity.Ratio(query, TextNormalizer.Normalize(name)));
                var romanized = TextNormalizer.RomanizeIfKana(name);
                if (romanized is not null)
                {
                    best = Math.Max(best, StringSimilarity.Ratio(query, romanized));
                }
            }
        }

        return best;
    }
}
