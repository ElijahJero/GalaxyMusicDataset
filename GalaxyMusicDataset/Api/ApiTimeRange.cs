using GalaxyMusicDataset.Services.Analytics;

namespace GalaxyMusicDataset.Api;

public static class ApiTimeRange
{
    public static TimeRange Resolve(
        AppTimeZone zone,
        string? range,
        string? from,
        string? to,
        DateTimeOffset? utcNow = null) =>
        TimeRangeParser.Parse(range, from, to, utcNow ?? DateTimeOffset.UtcNow, zone.Zone);
}
