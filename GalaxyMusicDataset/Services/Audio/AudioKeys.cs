namespace GalaxyMusicDataset.Services.Audio;

public static class AudioKeys
{
    private static readonly string[] Notes =
        ["C", "C#", "Db", "D", "D#", "Eb", "E", "F", "F#", "Gb", "G", "G#", "Ab", "A", "A#", "Bb", "B"];

    public static IReadOnlyList<string> CommonDisplays { get; } =
        Notes.SelectMany(note => new[] { $"{note} major", $"{note} minor" }).ToList();

    public static bool Matches(string? key, string? scale, string? query)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var (needleKey, needleScale) = AudioProfileMapper.SplitKey(query, null);
        if (string.IsNullOrWhiteSpace(needleKey))
        {
            return false;
        }

        if (!key.Equals(needleKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(needleScale))
        {
            return true;
        }

        return string.Equals(scale, needleScale, StringComparison.OrdinalIgnoreCase);
    }
}
