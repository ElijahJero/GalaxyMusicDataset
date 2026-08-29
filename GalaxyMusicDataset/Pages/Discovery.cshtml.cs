using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class DiscoveryModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "tracks";

    [BindProperty(SupportsGet = true)]
    public int Take { get; set; } = AnalyticsQueries.DefaultTake;

    public DiscoveryResult Result { get; private set; } = new([], []);
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Kind = Kind.Equals("artists", StringComparison.OrdinalIgnoreCase) ? "artists" : "tracks";
        if (Take < 1)
        {
            Take = AnalyticsQueries.DefaultTake;
        }

        Result = await analytics.GetDiscoveries(TimeRange, Q, Take, cancellationToken);
        SetChrome("discovery", Years, Extra());
    }

    public Dictionary<string, string?> Extra() => new() { ["kind"] = Kind, ["take"] = Take.ToString() };
}
