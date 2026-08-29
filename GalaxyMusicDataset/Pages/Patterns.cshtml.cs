using System.Text.Json;
using GalaxyMusicDataset.Services.Analytics;

namespace GalaxyMusicDataset.Pages;

public class PatternsModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    public HeatmapResult Heatmap { get; private set; } = new([], 0);
    public IReadOnlyList<TimeOfDayBucket> TimeOfDay { get; private set; } = [];
    public IReadOnlyList<MonthlyVolume> Monthly { get; private set; } = [];
    public string MonthlyJson { get; private set; } = "[]";
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Heatmap = await analytics.GetHeatmap(TimeRange, Q, cancellationToken);
        TimeOfDay = analytics.GetTimeOfDayBuckets(Heatmap);
        Monthly = await analytics.GetMonthlyVolume(TimeRange, Q, cancellationToken);
        MonthlyJson = JsonSerializer.Serialize(Monthly.Select(m => new
        {
            label = $"{m.Year}-{m.Month:00}",
            count = m.Count,
            minutes = Math.Round(m.DurationMs / 60000.0, 1)
        }));
        SetChrome("patterns", Years);
    }
}
