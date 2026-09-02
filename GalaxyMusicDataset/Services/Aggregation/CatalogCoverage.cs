using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.VocaDb;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed record SourceCoverage(
    string Name,
    int Hits,
    int Attempted,
    int Total,
    double HitPercent,
    bool Enabled,
    string? Note,
    bool ShowAttempted);

public static class CatalogCoverage
{
    public static double Percent(int hits, int total) =>
        total == 0 ? 0 : Math.Round(100.0 * hits / total, 1);

    public static SourceCoverage Field(
        string name,
        int hits,
        int total,
        bool enabled,
        string? note) =>
        new(name, hits, total, total, Percent(hits, total), enabled, note, false);

    public static SourceCoverage FromPayloads(
        string name,
        EnrichmentSource source,
        IReadOnlyList<SourcePayloadCount> payloads,
        int total,
        bool enabled,
        string? note)
    {
        var rows = payloads.Where(p =>
            string.Equals(p.Source, source.ToString(), StringComparison.OrdinalIgnoreCase));
        var hits = rows.Where(p => p.Status == nameof(SourceFetchStatus.Success)).Sum(p => p.Count);
        var attempted = rows
            .Where(p => p.Status != nameof(SourceFetchStatus.NotStarted))
            .Sum(p => p.Count);
        return new SourceCoverage(name, hits, attempted, total, Percent(hits, total), enabled, note, true);
    }

    public static IReadOnlyList<SourceCoverage> Build(
        int tracks,
        int withMbid,
        int withDuration,
        int withTags,
        IReadOnlyList<SourcePayloadCount> payloads,
        AggregationOptions aggregation,
        bool lastFmConfigured,
        bool discogsConfigured,
        bool audioDbConfigured)
    {
        string? Flag(bool enabled, string? missing) =>
            enabled ? (missing is null ? null : missing) : (missing is null ? "off" : $"off, {missing}");

        return
        [
            Field("MusicBrainz", withMbid, tracks, aggregation.EnableMusicBrainz,
                aggregation.EnableMusicBrainz ? null : "off"),
            FromPayloads(VocaDbFamily.DisplayName(EnrichmentSource.LastFm), EnrichmentSource.LastFm, payloads, tracks,
                aggregation.EnableLastFmTrackInfo && lastFmConfigured,
                Flag(aggregation.EnableLastFmTrackInfo, lastFmConfigured ? null : "no key")),
            FromPayloads("Discogs", EnrichmentSource.Discogs, payloads, tracks,
                aggregation.EnableDiscogs && discogsConfigured,
                Flag(aggregation.EnableDiscogs, discogsConfigured ? null : "no token")),
            FromPayloads("TheAudioDB", EnrichmentSource.TheAudioDb, payloads, tracks,
                aggregation.EnableTheAudioDb && audioDbConfigured,
                Flag(aggregation.EnableTheAudioDb, audioDbConfigured ? null : "no key")),
            FromPayloads("VocaDB", EnrichmentSource.VocaDb, payloads, tracks,
                aggregation.EnableVocaDb, aggregation.EnableVocaDb ? null : "off"),
            FromPayloads("UtaiteDB", EnrichmentSource.UtaiteDb, payloads, tracks,
                aggregation.EnableUtaiteDb, aggregation.EnableUtaiteDb ? null : "off"),
            FromPayloads("TouhouDB", EnrichmentSource.TouhouDb, payloads, tracks,
                aggregation.EnableTouhouDb, aggregation.EnableTouhouDb ? null : "off"),
            Field("Tags", withTags, tracks, true, null),
            Field("Duration", withDuration, tracks, true, null)
        ];
    }
}
