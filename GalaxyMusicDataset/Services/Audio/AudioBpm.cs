namespace GalaxyMusicDataset.Services.Audio;

/// <summary>
/// Play-weighted BPM histogram buckets used on Audio analytics and Library filters.
/// Ranges are half-open except the last bucket: 70 ≤ bpm &lt; 90 is <c>70–89</c>.
/// </summary>
public static class AudioBpm
{
    public static readonly IReadOnlyList<AudioBpmRange> Buckets =
    [
        new("<70", "lt70", null, 70),
        new("70–89", "70-89", 70, 90),
        new("90–109", "90-109", 90, 110),
        new("110–129", "110-129", 110, 130),
        new("130–149", "130-149", 130, 150),
        new("150+", "150plus", 150, null)
    ];

    public static AudioBpmRange Bucket(double bpm) =>
        Buckets.First(b => Matches(bpm, b.MinInclusive, b.MaxExclusive));

    public static bool Matches(double bpm, double? minInclusive, double? maxExclusive) =>
        (minInclusive is null || bpm >= minInclusive) &&
        (maxExclusive is null || bpm < maxExclusive);

    public static AudioBpmRange? Find(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var needle = Normalize(value);
        foreach (var bucket in Buckets)
        {
            if (Normalize(bucket.Slug) == needle || Normalize(bucket.Name) == needle)
            {
                return bucket;
            }
        }

        return null;
    }

    public static bool TryResolve(string? value, out AudioBpmRange range)
    {
        var found = Find(value);
        if (found is null)
        {
            range = default;
            return false;
        }

        range = found.Value;
        return true;
    }

    private static string Normalize(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Replace("–", "-", StringComparison.Ordinal)
            .Replace("—", "-", StringComparison.Ordinal)
            .Replace("to", "-", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("<", "lt", StringComparison.Ordinal);
        if (chars is "150+" or "150plus")
        {
            return "150plus";
        }

        return chars;
    }
}

public readonly record struct AudioBpmRange(
    string Name,
    string Slug,
    double? MinInclusive,
    double? MaxExclusive);
