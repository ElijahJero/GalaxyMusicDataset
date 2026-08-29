using System.Text.Json;
using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class TagsModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Name { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Take { get; set; } = AnalyticsQueries.DefaultTake;

    public TagCloudResult Cloud { get; private set; } = new([], [], 0, 0);
    public TagDetailResult? Detail { get; private set; }
    public string GenreJson { get; private set; } = "[]";
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Cloud = await analytics.GetTagCloud(TimeRange, Q, Take, cancellationToken);
        GenreJson = JsonSerializer.Serialize(Cloud.Genres.Select(t => new { label = t.Name, count = t.Plays }));
        if (!string.IsNullOrWhiteSpace(Name))
        {
            Detail = await analytics.GetTagDetail(Name, TimeRange, Q, Take, cancellationToken);
            if (Detail is null)
            {
                return NotFound();
            }
        }

        SetChrome("tags", Years);
        return Page();
    }

    public Dictionary<string, string?> Extra()
    {
        var extra = new Dictionary<string, string?> { ["take"] = Take.ToString() };
        if (!string.IsNullOrWhiteSpace(Name))
        {
            extra["name"] = Name;
        }

        return extra;
    }
}
