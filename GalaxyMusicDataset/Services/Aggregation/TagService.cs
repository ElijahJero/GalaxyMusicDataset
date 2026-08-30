using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class TagService(AppDbContext db)
{
    public async Task ApplyTagsAsync(
        long trackId,
        EnrichmentSource source,
        IEnumerable<(string Name, int Weight)> tags,
        CancellationToken cancellationToken)
    {
        foreach (var (name, weight) in CollapseByNormalizedName(tags))
        {
            var normalized = TextNormalizer.Normalize(name);
            var tag = db.Tags.Local.FirstOrDefault(t => t.NormalizedName == normalized)
                      ?? await db.Tags.FirstOrDefaultAsync(t => t.NormalizedName == normalized, cancellationToken);
            if (tag is null)
            {
                tag = new Tag { Name = name, NormalizedName = normalized };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(cancellationToken);
            }

            var link = db.TrackTags.Local.FirstOrDefault(t =>
                           t.TrackId == trackId && t.TagId == tag.Id && t.Source == source)
                       ?? await db.TrackTags.FirstOrDefaultAsync(
                           t => t.TrackId == trackId && t.TagId == tag.Id && t.Source == source,
                           cancellationToken);
            if (link is null)
            {
                db.TrackTags.Add(new TrackTag
                {
                    TrackId = trackId,
                    TagId = tag.Id,
                    Source = source,
                    Weight = weight
                });
            }
            else
            {
                link.Weight = weight;
            }
        }
    }

    // MusicBrainz genres are a subset of tags, so enrichment concatenates both
    // lists. Collapse to one row per normalized name before insert so we do not
    // violate TrackTags (TrackId, TagId, Source). Keep the highest weight
    // (genre 80 beats a small crowd-tag count).
    private static IEnumerable<(string Name, int Weight)> CollapseByNormalizedName(
        IEnumerable<(string Name, int Weight)> tags)
    {
        var unique = new Dictionary<string, (string Name, int Weight)>(StringComparer.Ordinal);
        foreach (var (name, weight) in tags)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var normalized = TextNormalizer.Normalize(name);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            if (!unique.TryGetValue(normalized, out var existing) || weight > existing.Weight)
            {
                unique[normalized] = (name.Trim(), weight);
            }
        }

        return unique.Values;
    }
}
