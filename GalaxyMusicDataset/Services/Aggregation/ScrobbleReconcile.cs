namespace GalaxyMusicDataset.Services.Aggregation;

public static class ScrobbleReconcile
{
    public const int NightlyDays = 7;
    public static readonly TimeSpan NightlyInterval = TimeSpan.FromHours(24);

    public static bool NightlyCheckDue(DateTimeOffset? lastCheckUtc, DateTimeOffset utcNow, bool paused)
    {
        if (paused)
        {
            return false;
        }

        return lastCheckUtc is null || utcNow - lastCheckUtc.Value >= NightlyInterval;
    }

    public static (long FromInclusive, long ToExclusive) UtcDayRange(DateTime utcDay)
    {
        var start = new DateTimeOffset(DateTime.SpecifyKind(utcDay.Date, DateTimeKind.Utc), TimeSpan.Zero);
        return (start.ToUnixTimeSeconds(), start.AddDays(1).ToUnixTimeSeconds());
    }

    /// <summary>
    /// Same inclusive Last.fm window the backfill uses, so a UTC day is fully covered.
    /// </summary>
    public static (long From, long To) LastFmWindow(long dayFromInclusive, long dayToExclusive) =>
        (dayFromInclusive - 1, dayToExclusive);
}
