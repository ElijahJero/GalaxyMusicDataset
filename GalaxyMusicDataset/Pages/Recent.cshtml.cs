using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalaxyMusicDataset.Pages;

public class RecentModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<RecentScrobbleRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var scrobbles = await db.Scrobbles
            .AsNoTracking()
            .Include(s => s.Track).ThenInclude(t => t.Artist).ThenInclude(a => a.Aliases)
            .Include(s => s.Track).ThenInclude(t => t.Album)
            .Include(s => s.Track).ThenInclude(t => t.Tags).ThenInclude(t => t.Tag)
            .Include(s => s.Track).ThenInclude(t => t.SourcePayloads)
            .OrderByDescending(s => s.UnixTimestamp)
            .Take(50)
            .ToListAsync(cancellationToken);

        var fingerprints = scrobbles.Select(s => s.Track.Fingerprint).Distinct().ToList();
        var lookups = await db.TrackLookups.AsNoTracking()
            .Where(l => fingerprints.Contains(l.Fingerprint))
            .ToListAsync(cancellationToken);
        var lookupByFp = lookups.ToDictionary(l => l.Fingerprint);

        Rows = scrobbles.Select(s => new RecentScrobbleRow(
            s,
            lookupByFp.GetValueOrDefault(s.Track.Fingerprint))).ToList();
    }
}

public sealed record RecentScrobbleRow(Scrobble Scrobble, TrackLookup? Lookup);
