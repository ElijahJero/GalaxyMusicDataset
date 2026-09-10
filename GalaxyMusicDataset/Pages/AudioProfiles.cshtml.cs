using System.Text.Json;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class AudioProfilesModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Name { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Take { get; set; } = AnalyticsQueries.DefaultTake;

    public AudioAnalyticsResult Audio { get; private set; } = null!;
    public AudioLabelDetailResult? Detail { get; private set; }
    public IReadOnlyList<int> Years { get; private set; } = [];
    public string BpmJson { get; private set; } = "[]";
    public string KeyJson { get; private set; } = "[]";
    public string MoodJson { get; private set; } = "[]";
    public string GenreJson { get; private set; } = "[]";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        Audio = await analytics.GetAudioAnalytics(TimeRange, Q, Take, cancellationToken);
        BpmJson = JsonSerializer.Serialize(Audio.BpmBuckets.Select(b => new { label = b.Name, count = b.Plays }));
        KeyJson = JsonSerializer.Serialize(Audio.Keys.Select(k => new { label = k.Name, count = k.Plays }));
        MoodJson = JsonSerializer.Serialize(Audio.MoodAverages.Select(m => new { label = m.Name, count = Math.Round(m.Value, 3) }));
        GenreJson = JsonSerializer.Serialize(Audio.Genres.Select(t => new { label = t.Name, count = t.Plays }));
        if (!string.IsNullOrWhiteSpace(Name))
        {
            if (!Enum.TryParse<AudioLabelKind>(Kind, true, out var kind))
            {
                return NotFound();
            }

            Detail = await analytics.GetAudioLabelDetail(kind, Name, TimeRange, Q, Take, cancellationToken);
            if (Detail is null)
            {
                return NotFound();
            }
        }

        SetChrome("audio", Years);
        return Page();
    }

    public Dictionary<string, string?> Extra()
    {
        var extra = new Dictionary<string, string?> { ["take"] = Take.ToString() };
        if (!string.IsNullOrWhiteSpace(Kind))
        {
            extra["kind"] = Kind;
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            extra["name"] = Name;
        }

        return extra;
    }
}
