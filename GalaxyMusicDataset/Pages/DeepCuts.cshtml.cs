using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class DeepCutsModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    [BindProperty(SupportsGet = true)]
    public int N { get; set; } = AnalyticsQueries.DefaultHeavyThreshold;

    [BindProperty(SupportsGet = true)]
    public int Take { get; set; } = AnalyticsQueries.DefaultTake;

    public DeepCutsResult Result { get; private set; } = new([], 0, [], 0, AnalyticsQueries.DefaultHeavyThreshold, false, false);
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        if (N < 2)
        {
            N = AnalyticsQueries.DefaultHeavyThreshold;
        }

        if (Take < 1)
        {
            Take = AnalyticsQueries.DefaultTake;
        }

        Result = await analytics.GetDeepCuts(TimeRange, Q, N, Take, cancellationToken);
        SetChrome("deepcuts", Years, Extra());
    }

    public Dictionary<string, string?> Extra() => new() { ["n"] = N.ToString(), ["take"] = Take.ToString() };
}
