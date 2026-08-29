using System.Globalization;

namespace GalaxyMusicDataset.Services.Analytics;

public static class TimeRangeParser
{
    public static readonly string[] Presets = ["7d", "30d", "90d", "1y", "all", "custom"];

    public static TimeRange Parse(string? preset, string? from, string? to, DateTimeOffset utcNow)
    {
        var key = (preset ?? "30d").Trim().ToLowerInvariant();
        if (key == "custom")
        {
            if (TryParseDate(from, out var fromDate) && TryParseDate(to, out var toDate))
            {
                var start = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var end = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                if (end <= start)
                {
                    end = start.AddDays(1);
                }

                return new TimeRange(start, end, "custom");
            }

            key = "30d";
        }

        var toExclusive = utcNow.AddSeconds(1);
        return key switch
        {
            "7d" => new TimeRange(utcNow.AddDays(-7), toExclusive, "7d"),
            "90d" => new TimeRange(utcNow.AddDays(-90), toExclusive, "90d"),
            "1y" => new TimeRange(utcNow.AddYears(-1), toExclusive, "1y"),
            "all" => new TimeRange(DateTimeOffset.UnixEpoch, toExclusive, "all"),
            _ => new TimeRange(utcNow.AddDays(-30), toExclusive, "30d")
        };
    }

    public static TimeRange ForCalendarYear(int year)
    {
        var start = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeRange(start, end, "custom");
    }

    public static TimeRange PreviousWindow(TimeRange range)
    {
        var length = range.To - range.From;
        if (length <= TimeSpan.Zero)
        {
            length = TimeSpan.FromDays(30);
        }

        return new TimeRange(range.From - length, range.From, "previous");
    }

    public static int CalendarDays(TimeRange range) =>
        Math.Max(1, (int)Math.Ceiling((range.To - range.From).TotalDays));

    public static string IsoDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            date = DateOnly.FromDateTime(dto.UtcDateTime);
            return true;
        }

        date = default;
        return false;
    }
}
