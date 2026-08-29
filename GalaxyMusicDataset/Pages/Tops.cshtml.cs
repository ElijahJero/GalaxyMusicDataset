using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class TopsModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "artists";

    [BindProperty(SupportsGet = true)]
    public int Take { get; set; } = AnalyticsQueries.DefaultTake;

    public TopListResult Result { get; private set; } = new([], 0, false);
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ResolveFilter();
        Years = await analytics.GetYears(cancellationToken);
        if (Take < 1)
        {
            Take = AnalyticsQueries.DefaultTake;
        }

        Tab = Tab.ToLowerInvariant() switch
        {
            "tracks" => "tracks",
            "albums" => "albums",
            _ => "artists"
        };

        var previous = TimeRange.Preset == "all" ? null : TimeRangeParser.PreviousWindow(TimeRange);
        Result = Tab switch
        {
            "tracks" => await analytics.GetTopTracks(TimeRange, previous, Q, Take, cancellationToken),
            "albums" => await analytics.GetTopAlbums(TimeRange, previous, Q, Take, cancellationToken),
            _ => await analytics.GetTopArtists(TimeRange, previous, Q, Take, cancellationToken)
        };

        SetChrome("tops", Years);
    }

    public Dictionary<string, string?> Extra() => new() { ["tab"] = Tab, ["take"] = Take.ToString() };
}
