using System.Globalization;

namespace GalaxyMusicDataset.Services.Analytics;

public static class AnalyticsDisplay
{
    public static string Duration(long? ms)
    {
        if (ms is null or <= 0)
        {
            return "—";
        }

        var ts = TimeSpan.FromMilliseconds(ms.Value);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }

        return $"{ts.Seconds}s";
    }

    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Timestamp(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    public static string Percent(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public static string Delta(RankedItem item)
    {
        if (item.IsNew)
        {
            return "new";
        }

        var sign = item.Delta > 0 ? "+" : "";
        if (item.PercentChange is double pct)
        {
            return $"{sign}{item.Delta} ({sign}{pct.ToString("0.#", CultureInfo.InvariantCulture)}%)";
        }

        return $"{sign}{item.Delta}";
    }

    public static string Average(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static DateOnly UtcDay(long unixTimestamp) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime);
}
