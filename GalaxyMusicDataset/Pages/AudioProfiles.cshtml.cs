using System.Text.Json;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

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
        BpmJson = ChartPayload(Audio.BpmBuckets, bucket => AudioFeatureQuery("bpm", AudioBpm.Find(bucket.Name)?.Slug ?? bucket.Name));
        KeyJson = ChartPayload(Audio.Keys, key => AudioFeatureQuery("key", key.Name));
        MoodJson = JsonSerializer.Serialize(Audio.MoodAverages.Select(m => new { label = m.Name, count = Math.Round(m.Value, 3) }));
        GenreJson = ChartPayload(Audio.Genres.Select(t => new NamedCount(t.Name, t.TrackCount, t.Plays)), genre => AudioLabelQuery(AudioLabelKind.Genre, genre.Name));
        if (!string.IsNullOrWhiteSpace(Name))
        {
            Detail = await analytics.GetAudioLabelDetail(Kind, Name, TimeRange, Q, Take, cancellationToken);
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

    public Dictionary<string, string?> LibraryFilterForDetail()
    {
        var routes = new Dictionary<string, string?> { ["sort"] = "plays" };
        if (Detail is null)
        {
            return routes;
        }

        switch (Detail.Kind)
        {
            case "bpm":
                routes["bpmBucket"] = Detail.Path;
                break;
            case "key":
                routes["audioKey"] = Detail.Path;
                break;
            default:
                routes["audioKind"] = Detail.Kind;
                routes["audioLabel"] = Detail.Path;
                break;
        }

        return routes;
    }

    public string DetailKindLabel => Detail?.Kind switch
    {
        "bpm" => "range",
        "key" => "key",
        _ when Detail?.IsFolder == true => "folder",
        _ => "tag"
    };

    private string ChartPayload(IEnumerable<NamedCount> rows, Func<NamedCount, Dictionary<string, string?>> href) =>
        JsonSerializer.Serialize(rows.Select(row =>
        {
            var routes = new RouteValueDictionary();
            foreach (var pair in href(row))
            {
                if (!string.IsNullOrEmpty(pair.Value))
                {
                    routes[pair.Key] = pair.Value;
                }
            }

            return new
            {
                label = row.Name,
                count = row.Plays,
                href = Url.Page("/AudioProfiles", routes)
            };
        }));
}
