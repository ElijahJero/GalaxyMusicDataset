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
            Year = Years.FirstOrDefault(DateTime.UtcNow.Year);
            return RedirectToPage(new { year = Year, q = Q });
        }

        TimeRange = TimeRangeParser.ForCalendarYear(Year.Value);
        Range = "custom";
        From = TimeRangeParser.IsoDate(TimeRange.From);
        To = TimeRangeParser.IsoDate(TimeRange.To.AddSeconds(-1));
        Result = await analytics.GetWrapped(Year.Value, Q, cancellationToken);
        SetChrome("wrapped", Years);
        return Page();
    }
}
