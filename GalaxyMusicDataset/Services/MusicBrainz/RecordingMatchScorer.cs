using GalaxyMusicDataset.Services.Normalization;

namespace GalaxyMusicDataset.Services.MusicBrainz;

public sealed record RecordingCandidate(
    string Mbid,
    string Title,
    string Artist,
    string? Album,
    int? LengthMs,
    string? ArtistMbid,
    string? ReleaseMbid,
    string? Disambiguation,
    double Score);

public static class RecordingMatchScorer
{
    public static double Score(
        string queryArtist,
        string queryTitle,
        string? queryAlbum,
        RecordingCandidate candidate)
    {
        var artist = StringSimilarity.Ratio(
            TextNormalizer.Normalize(queryArtist),
            TextNormalizer.Normalize(candidate.Artist));
        var title = StringSimilarity.Ratio(
            TextNormalizer.Normalize(queryTitle),
            TextNormalizer.Normalize(candidate.Title));

        double album = 1;
        var albumWeight = 0.0;
        if (!string.IsNullOrWhiteSpace(queryAlbum) && !string.IsNullOrWhiteSpace(candidate.Album))
        {
            album = StringSimilarity.Ratio(
                TextNormalizer.Normalize(queryAlbum),
                TextNormalizer.Normalize(candidate.Album));
            albumWeight = 0.15;
        }

        var titleWeight = 0.5 + ((0.15 - albumWeight) / 2);
        var artistWeight = 0.35 + ((0.15 - albumWeight) / 2);
        var score = (titleWeight * title) + (artistWeight * artist) + (albumWeight * album);

        if (artist >= 0.98 && title >= 0.98)
        {
            score = Math.Max(score, 0.95);
        }

        return Math.Clamp(score, 0, 1);
    }

    public static LookupDecision Decide(double bestScore, double artistScore, double autoThreshold, double reviewThreshold)
    {
        if (bestScore >= autoThreshold && artistScore >= 0.85)
        {
            return LookupDecision.AutoMatch;
        }

        if (bestScore >= reviewThreshold)
        {
            return LookupDecision.NeedsReview;
        }

        return LookupDecision.NotFound;
    }
}

public enum LookupDecision
{
    AutoMatch,
    NeedsReview,
    NotFound
}
