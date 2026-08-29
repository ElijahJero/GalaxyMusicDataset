using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class TracksModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    public TrackDetail Detail { get; private set; } = null!;
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        var detail = await analytics.GetTrackDetail(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        SetChrome("track", Years);
        return Page();
    }
}
