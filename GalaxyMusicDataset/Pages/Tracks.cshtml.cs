using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class TracksModel(AnalyticsQueries analytics, MetadataEnrichmentService enrichment) : AnalyticsPageModel
{
    public TrackDetail Detail { get; private set; } = null!;
    public IReadOnlyList<int> Years { get; private set; } = [];
    public string? Flash { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Flash = TempData["TrackFlash"] as string;
        var detail = await analytics.GetTrackDetail(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        SetChrome("track", Years);
        return Page();
    }

    public async Task<IActionResult> OnPostLookupMbidAsync(long id, CancellationToken cancellationToken)
    {
        TempData["TrackFlash"] = await enrichment.EnrichTrackFromMbidAsync(id, cancellationToken);
        return RedirectToPage(DetailRoute(id));
    }
}
