using System.Text.Json;
using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class ArtistsModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    public ArtistDetail Detail { get; private set; } = null!;
    public string TimelineJson { get; private set; } = "[]";
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        var detail = await analytics.GetArtistDetail(id, TimeRange, Q, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        TimelineJson = JsonSerializer.Serialize(detail.Timeline.Select(d => new { day = d.Day.ToString("yyyy-MM-dd"), count = d.Count }));
        SetChrome("artist", Years);
        return Page();
    }
}
