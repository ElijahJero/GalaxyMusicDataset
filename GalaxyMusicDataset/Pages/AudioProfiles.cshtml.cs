using System.Text.Json;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;

namespace GalaxyMusicDataset.Pages;

public class AudioProfilesModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    public AudioAnalyticsResult Audio { get; private set; } = null!;
    public IReadOnlyList<int> Years { get; private set; } = [];
    public string BpmJson { get; private set; } = "[]";
    public string KeyJson { get; private set; } = "[]";
    public string MoodJson { get; private set; } = "[]";
    public string GenreJson { get; private set; } = "[]";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Audio = await analytics.GetAudioAnalytics(TimeRange, Q, 20, cancellationToken);
        BpmJson = JsonSerializer.Serialize(Audio.BpmBuckets.Select(b => new { label = b.Name, count = b.Plays }));
        KeyJson = JsonSerializer.Serialize(Audio.Keys.Select(k => new { label = k.Name, count = k.Plays }));
        MoodJson = JsonSerializer.Serialize(Audio.MoodAverages.Select(m => new { label = m.Name, count = Math.Round(m.Value, 3) }));
        GenreJson = JsonSerializer.Serialize(Audio.Genres.Select(t => new { label = t.Name, count = t.Plays }));
        SetChrome("audio", Years);
    }
}
