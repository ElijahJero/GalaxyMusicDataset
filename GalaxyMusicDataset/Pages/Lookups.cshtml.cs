using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Pages;

public class LookupsModel(AppDbContext db, MusicBrainzLookupService lookups) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public IReadOnlyList<TrackLookup> Rows { get; private set; } = [];
    public Dictionary<string, int> Counts { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var groups = await db.TrackLookups
            .GroupBy(l => l.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        Counts = groups.ToDictionary(x => x.Key.ToString(), x => x.Count);

        var query = db.TrackLookups.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Status) && Enum.TryParse<LookupStatus>(Status, out var parsed))
        {
            query = query.Where(l => l.Status == parsed);
        }

        Rows = await query.OrderByDescending(l => l.Id).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRetryAsync(long id, CancellationToken cancellationToken)
    {
        await lookups.RetryAsync(id, cancellationToken);
        return RedirectToPage(new { Status });
    }
}
