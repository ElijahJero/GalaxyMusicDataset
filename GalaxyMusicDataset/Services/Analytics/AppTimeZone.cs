using GalaxyMusicDataset.Configuration;

namespace GalaxyMusicDataset.Services.Analytics;

public sealed class AppTimeZone
{
    public const string DefaultId = "America/New_York";

    public static AppTimeZone Utc { get; } = new(TimeZoneInfo.Utc, "UTC");

    public static AppTimeZone Eastern { get; } = FromId(DefaultId);

    public TimeZoneInfo Zone { get; }

    public string Id { get; }

    /// <summary>Short label for UI chrome (EST for Eastern, UTC, or the configured id).</summary>
    public string DisplayLabel { get; }

    private AppTimeZone(TimeZoneInfo zone, string id)
    {
        Zone = zone;
        Id = id;
        DisplayLabel = LabelForZone(zone, id);
    }

    public static AppTimeZone FromOptions(AnalyticsOptions options) => FromId(options.TimeZone);

    public static AppTimeZone FromId(string? id)
    {
        var raw = string.IsNullOrWhiteSpace(id) ? DefaultId : id.Trim();
        var mapped = MapAlias(raw);
        return new AppTimeZone(FindZone(mapped), mapped);
    }

    public DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Zone);

    public DateTimeOffset ToLocal(long unixTimestamp) =>
        ToLocal(DateTimeOffset.FromUnixTimeSeconds(unixTimestamp));

    public DateOnly LocalDate(long unixTimestamp) => DateOnly.FromDateTime(ToLocal(unixTimestamp).DateTime);

    public DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(ToLocal(value).DateTime);

    public int LocalHour(long unixTimestamp) => ToLocal(unixTimestamp).Hour;

    public int LocalYear(long unixTimestamp) => ToLocal(unixTimestamp).Year;

    public int WeekdayMonday0(long unixTimestamp)
    {
        var dow = (int)ToLocal(unixTimestamp).DayOfWeek;
        return (dow + 6) % 7;
    }

    public DateTimeOffset StartOfLocalDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public string Abbreviation(DateTimeOffset utc)
    {
        if (Zone.Equals(TimeZoneInfo.Utc) || Zone.Id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        if (IsEastern(Id, Zone.Id))
        {
            return Zone.IsDaylightSavingTime(utc.UtcDateTime) ? "EDT" : "EST";
        }

        return Zone.IsDaylightSavingTime(utc.UtcDateTime)
            ? NonEmpty(Zone.DaylightName, DisplayLabel)
            : NonEmpty(Zone.StandardName, DisplayLabel);
    }

    private static IEnumerable<string> Fallbacks(string id)
    {
        if (id.Equals(DefaultId, StringComparison.OrdinalIgnoreCase)
            || id.Equals("US/Eastern", StringComparison.OrdinalIgnoreCase)
            || id.Equals("Eastern Standard Time", StringComparison.OrdinalIgnoreCase))
        {
            yield return DefaultId;
            yield return "Eastern Standard Time";
            yield return "US/Eastern";
        }
    }

    private static string MapAlias(string id)
    {
        return id.Trim().ToUpperInvariant() switch
        {
            "EST" or "EDT" or "ET" or "EASTERN" or "US/EASTERN" => DefaultId,
            "UTC" or "GMT" or "Z" or "UCT" => "UTC",
            _ => id.Trim()
        };
    }

    private static TimeZoneInfo FindZone(string id)
    {
        if (id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            foreach (var fallback in Fallbacks(id))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(fallback);
                }
                catch (Exception nested) when (nested is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    // try next
                }
            }

            return TimeZoneInfo.CreateCustomTimeZone("EST", TimeSpan.FromHours(-5), "Eastern Standard Time", "EST");
        }
    }

    private static string LabelForZone(TimeZoneInfo zone, string id)
    {
        if (zone.Equals(TimeZoneInfo.Utc) || id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        if (IsEastern(id, zone.Id))
        {
            return "EST";
        }

        return id;
    }

    private static bool IsEastern(string configuredId, string zoneId) =>
        configuredId.Equals(DefaultId, StringComparison.OrdinalIgnoreCase)
        || configuredId.Equals("US/Eastern", StringComparison.OrdinalIgnoreCase)
        || zoneId.Equals("Eastern Standard Time", StringComparison.OrdinalIgnoreCase)
        || zoneId.Equals(DefaultId, StringComparison.OrdinalIgnoreCase);

    private static string NonEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
