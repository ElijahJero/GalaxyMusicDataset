using System.Text;
using GalaxyMusicDataset.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Pages;

public class WrappedModel(AnalyticsQueries analytics) : AnalyticsPageModel
{
    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public WrappedResult? Result { get; private set; }
    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Years = await analytics.GetYears(cancellationToken);
        if (Year is null or < 1970)
        {
            Year = Years.FirstOrDefault(DisplayTimeZone.LocalDate(DateTimeOffset.UtcNow).Year);
            return RedirectToPage(new { year = Year, q = Q });
        }

        TimeRange = TimeRangeParser.ForCalendarYear(Year.Value, DisplayTimeZone.Zone);
        Range = "custom";
        From = TimeRangeParser.IsoDate(TimeRange.From, DisplayTimeZone.Zone);
        To = TimeRangeParser.IsoDate(TimeRange.To.AddSeconds(-1), DisplayTimeZone.Zone);
        Result = await analytics.GetWrapped(Year.Value, Q, cancellationToken);
        SetChrome("wrapped", Years);
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(CancellationToken cancellationToken)
    {
        Years = await analytics.GetYears(cancellationToken);
        if (Year is null or < 1970)
        {
            Year = Years.FirstOrDefault(DisplayTimeZone.LocalDate(DateTimeOffset.UtcNow).Year);
            return RedirectToPage(new { year = Year, q = Q, handler = "Download" });
        }

        var export = await analytics.GetWrappedHtmlExport(Year.Value, Q, cancellationToken);
        var html = WrappedHtmlGenerator.Generate(export);
        var bytes = Encoding.UTF8.GetBytes(html);
        return File(bytes, "text/html; charset=utf-8", $"wrapped-{Year}.html");
    }
}
