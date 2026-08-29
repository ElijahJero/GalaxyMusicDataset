using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class SessionsModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    [BindProperty(SupportsGet = true)]
    public int Gap { get; set; } = AnalyticsQueries.DefaultSessionGapMinutes;

    [BindProperty(SupportsGet = true)]
    public int Take { get; set; } = AnalyticsQueries.DefaultTake;

    public SessionsResult Result { get; private set; } = new([], 0, false, 0, 0, 0, 0, 0);
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        if (Gap <= 0)
        {
            Gap = AnalyticsQueries.DefaultSessionGapMinutes;
        }

        if (Take < 1)
        {
            Take = AnalyticsQueries.DefaultTake;
        }

        Result = await analytics.GetSessions(TimeRange, Q, Gap, Take, cancellationToken);
        SetChrome("sessions", Years, Extra());
    }

    public Dictionary<string, string?> Extra() => new() { ["gap"] = Gap.ToString(), ["take"] = Take.ToString() };
}
