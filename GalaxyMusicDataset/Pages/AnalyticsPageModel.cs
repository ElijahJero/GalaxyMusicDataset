using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalaxyMusicDataset.Pages;

public abstract class AnalyticsPageModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Range { get; set; } = "30d";

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public TimeRange TimeRange { get; protected set; } = TimeRangeParser.Parse("30d", null, null, DateTimeOffset.UtcNow);

    protected void ResolveFilter(DateTimeOffset? utcNow = null)
    {
        TimeRange = TimeRangeParser.Parse(Range, From, To, utcNow ?? DateTimeOffset.UtcNow);
        Range = TimeRange.Preset;
        if (TimeRange.Preset == "custom")
        {
            From ??= TimeRangeParser.IsoDate(TimeRange.From);
            To ??= TimeRangeParser.IsoDate(TimeRange.To.AddSeconds(-1));
        }
    }

    public Dictionary<string, string?> FilterQuery(IReadOnlyDictionary<string, string?>? extra = null)
    {
        var d = new Dictionary<string, string?> { ["range"] = Range };
        if (Range == "custom")
        {
            d["from"] = From;
            d["to"] = To;
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            d["q"] = Q;
        }

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    d[key] = value;
                }
            }
        }

        return d;
    }

    protected void SetChrome(string navKey, IReadOnlyList<int>? years = null, IReadOnlyDictionary<string, string?>? extra = null)
    {
        ViewData["AnalyticsNav"] = navKey;
        ViewData["FilterQuery"] = FilterQuery(extra);
        ViewData["Years"] = years ?? [];
    }

    public TimeRangeViewModel TimeRangeView(string page, IReadOnlyDictionary<string, string?>? extra = null, long? routeId = null) =>
        new(page, Range, From, To, Q, extra ?? new Dictionary<string, string?>(), TimeRange.From, TimeRange.To, routeId);
}

public sealed record TimeRangeViewModel(
    string Page,
    string Preset,
    string? From,
    string? To,
    string? Q,
    IReadOnlyDictionary<string, string?> Extra,
    DateTimeOffset RangeFrom,
    DateTimeOffset RangeTo,
    long? RouteId = null);

public sealed record TopTableModel(
    IReadOnlyList<RankedItem> Items,
    Dictionary<string, string?> Filter,
    string NameHeader,
    string? DetailPage,
    bool ShowMovers);
