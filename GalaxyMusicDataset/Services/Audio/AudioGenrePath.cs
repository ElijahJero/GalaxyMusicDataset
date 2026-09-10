using GalaxyMusicDataset.Data;

namespace GalaxyMusicDataset.Services.Audio;

/// <summary>
/// Discogs-400 labels are <c>Primary---Sub</c> (pretty-printed as <c>Primary/Sub</c>).
/// Primaries such as <c>Funk / Soul</c> keep spaced slashes, so hierarchy uses an unspaced
/// <c>/</c> (or leftover <c>---</c>) as the folder separator.
/// </summary>
public static class AudioGenrePath
{
    public const string StoredSeparator = "---";
    public const char DisplaySeparator = '/';

    public static (string Primary, string? Sub) Split(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ("", null);
        }

        var value = name.Trim();
        var stored = value.IndexOf(StoredSeparator, StringComparison.Ordinal);
        if (stored >= 0)
        {
            return SplitAt(value, stored, StoredSeparator.Length);
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != DisplaySeparator)
            {
                continue;
            }

            var leftSpace = i > 0 && value[i - 1] == ' ';
            var rightSpace = i + 1 < value.Length && value[i + 1] == ' ';
            if (leftSpace || rightSpace)
            {
                continue;
            }

            return SplitAt(value, i, 1);
        }

        return (value, null);
    }

    public static string Join(string primary, string? sub)
    {
        primary = primary.Trim();
        if (string.IsNullOrWhiteSpace(sub))
        {
            return primary;
        }

        return $"{primary}{DisplaySeparator}{sub.Trim()}";
    }

    public static bool HasHierarchySeparator(string? name)
    {
        var (primary, sub) = Split(name);
        return !string.IsNullOrEmpty(primary) && sub is not null;
    }

    public static bool IsFolderQuery(AudioLabelKind kind, string? query) =>
        kind == AudioLabelKind.Genre && !string.IsNullOrWhiteSpace(query) && !HasHierarchySeparator(query);

    public static bool Matches(string storedName, AudioLabelKind kind, string query)
    {
        if (string.IsNullOrWhiteSpace(storedName) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        if (string.Equals(storedName, query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IsFolderQuery(kind, query))
        {
            return false;
        }

        var (primary, _) = Split(storedName);
        return string.Equals(primary, query.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplayName(string storedName, string query)
    {
        if (string.Equals(storedName, query, StringComparison.OrdinalIgnoreCase))
        {
            return storedName;
        }

        var (primary, sub) = Split(storedName);
        if (IsFolderQuery(AudioLabelKind.Genre, query))
        {
            return primary;
        }

        return Join(primary, sub);
    }

    private static (string Primary, string? Sub) SplitAt(string value, int index, int separatorLength)
    {
        var primary = value[..index].Trim();
        var sub = value[(index + separatorLength)..].Trim();
        if (string.IsNullOrEmpty(primary))
        {
            return (value, null);
        }

        return string.IsNullOrEmpty(sub) ? (primary, null) : (primary, sub);
    }
}
