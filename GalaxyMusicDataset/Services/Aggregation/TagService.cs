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

            var tag = await db.Tags.FirstOrDefaultAsync(t => t.NormalizedName == normalized, cancellationToken);
            if (tag is null)
            {
                tag = new Tag { Name = name.Trim(), NormalizedName = normalized };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(cancellationToken);
            }

            var link = await db.TrackTags.FirstOrDefaultAsync(
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
}
