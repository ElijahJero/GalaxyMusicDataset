using System.Text.Json;
using GalaxyMusicDataset.Services.Analytics;

namespace GalaxyMusicDataset.Pages;

public class DashboardModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    public OverviewStats Overview { get; private set; } = null!;
    public TagCloudResult Tags { get; private set; } = new([], [], 0, 0);
    public string DailyJson { get; private set; } = "[]";
    public string GenreJson { get; private set; } = "[]";
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Overview = await analytics.GetOverview(TimeRange, Q, cancellationToken);
        Tags = await analytics.GetTagCloud(TimeRange, Q, 8, cancellationToken);
        DailyJson = JsonSerializer.Serialize(Overview.DailyVolume.Select(d => new { day = d.Day.ToString("yyyy-MM-dd"), count = d.Count }));
        GenreJson = JsonSerializer.Serialize(Tags.Genres.Select(t => new { label = t.Name, count = t.Plays }));
        SetChrome("dashboard", Years);
    }
}
