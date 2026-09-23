using System.Text.Json;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Pages;

public class DashboardModel(AnalyticsQueries analytics, AppDbContext db) : AnalyticsPageModel
{
    public OverviewStats Overview { get; private set; } = null!;
    public TagCloudResult Tags { get; private set; } = new([], [], 0, 0);
    public AudioAnalyticsResult Audio { get; private set; } = null!;
    public string DailyJson { get; private set; } = "[]";
    public string GenreJson { get; private set; } = "[]";
    public IReadOnlyList<int> Years { get; private set; } = [];
    public string DatabaseSizeLabel { get; private set; } = "0 B";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Overview = await analytics.GetOverview(TimeRange, Q, cancellationToken);
        Tags = await analytics.GetTagCloud(TimeRange, Q, 8, cancellationToken);
        Audio = await analytics.GetAudioAnalytics(TimeRange, Q, 8, cancellationToken);
        DailyJson = JsonSerializer.Serialize(Overview.DailyVolume.Select(d => new { day = d.Day.ToString("yyyy-MM-dd"), count = d.Count }));
        GenreJson = JsonSerializer.Serialize(Tags.Genres.Select(t => new { label = t.Name, count = t.Plays }));
        DatabaseSizeLabel = DatabaseSize.Format(DatabaseSize.OnDiskBytes(db.Database.GetDbConnection().DataSource));
        SetChrome("dashboard", Years);
    }
}
