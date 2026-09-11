using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Audio;

public sealed class AudioProfileService(AppDbContext db)
{
    public const int DefaultPendingTake = 25;
    public const int MaxPendingTake = 100;

    public async Task<IReadOnlyList<PendingAudioTrack>> ListPendingAsync(int take, CancellationToken cancellationToken)
    {
        take = take is < 1 or > MaxPendingTake ? DefaultPendingTake : take;
        return await db.Tracks.AsNoTracking()
            .Where(t => t.AudioProfile == null)
            .OrderByDescending(t => t.Scrobbles.Count())
            .ThenBy(t => t.Title)
            .Take(take)
            .Select(t => new PendingAudioTrack(
                t.Id,
                t.Artist.Name,
                t.Title,
                t.Album != null ? t.Album.Title : null,
                t.Mbid,
                t.Scrobbles.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<AudioProfileView?> GetAsync(long trackId, CancellationToken cancellationToken)
    {
        var profile = await db.TrackAudioProfiles.AsNoTracking()
            .Include(p => p.Labels)
            .FirstOrDefaultAsync(p => p.TrackId == trackId, cancellationToken);
        return profile is null ? null : ToView(profile);
    }

    public async Task<AudioProfileView> UpsertAsync(long trackId, AudioProfileWriteRequest request, CancellationToken cancellationToken)
    {
        var exists = await db.Tracks.AnyAsync(t => t.Id == trackId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Track not found.");
        }

        return await SaveAsync(trackId, request, cancellationToken);
    }

    public async Task<AudioProfileView> UpsertByNamesAsync(
        string? artist,
        string? title,
        AudioProfileWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Artist and title are required to match a track.");
        }

        var fingerprint = TrackFingerprint.Compute(artist, title);
        var trackId = await db.Tracks
            .Where(t => t.Fingerprint == fingerprint)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (trackId is null)
        {
            throw new InvalidOperationException("No track matches that artist and title.");
        }

        return await SaveAsync(trackId.Value, request, cancellationToken);
    }

    private async Task<AudioProfileView> SaveAsync(long trackId, AudioProfileWriteRequest request, CancellationToken cancellationToken)
    {
        var parsed = AudioProfileMapper.Parse(request);
        var profile = await db.TrackAudioProfiles
            .Include(p => p.Labels)
            .FirstOrDefaultAsync(p => p.TrackId == trackId, cancellationToken);
        if (profile is null)
        {
            profile = new TrackAudioProfile { TrackId = trackId };
            db.TrackAudioProfiles.Add(profile);
        }
        else if (profile.Labels.Count > 0)
        {
            db.TrackAudioLabels.RemoveRange(profile.Labels.ToList());
            profile.Labels.Clear();
        }

        AudioProfileMapper.Apply(profile, parsed);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(profile);
    }

    public static AudioProfileView ToView(TrackAudioProfile profile)
    {
        var genres = Labels(profile, AudioLabelKind.Genre);
        var themes = Labels(profile, AudioLabelKind.Theme);
        var instruments = Labels(profile, AudioLabelKind.Instrument);
        return new AudioProfileView(
            profile.AnalyzedAt,
            profile.Bpm,
            profile.Key,
            profile.Scale,
            AudioProfileMapper.KeyDisplay(profile.Key, profile.Scale),
            profile.KeyStrength,
            profile.Loudness,
            profile.Danceability,
            profile.Acoustic,
            profile.Electronic,
            profile.Voice,
            profile.Instrumental,
            profile.Tonal,
            profile.Timbre,
            profile.TimbreBright,
            profile.Approachability,
            profile.Engagement,
            profile.MoodHappy,
            profile.MoodSad,
            profile.MoodAggressive,
            profile.MoodRelaxed,
            profile.MoodParty,
            AudioProfileMapper.TopMood(profile),
            genres,
            themes,
            instruments,
            AudioProfileMapper.FormatSummary(profile, themes, instruments),
            profile.RawJson);
    }

    private static IReadOnlyList<AudioLabelView> Labels(TrackAudioProfile profile, AudioLabelKind kind) =>
        profile.Labels
            .Where(l => l.Kind == kind)
            .OrderByDescending(l => l.Score ?? 0)
            .ThenBy(l => l.Name)
            .Select(l => new AudioLabelView(l.Name, l.Score))
            .ToList();
}

public static class AudioProfileMapper
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ParsedAudioProfile Parse(AudioProfileWriteRequest request)
    {
        var (key, scale) = SplitKey(request.Key, request.Scale);
        var genres = ExtraJson(request, "genre_scores") ?? request.Genres;
        return new ParsedAudioProfile(
            request.Bpm,
            key,
            scale,
            request.KeyStrength ?? ExtraNumber(request, "key_strength"),
            request.Loudness,
            request.Danceability,
            request.Acoustic,
            request.Electronic,
            request.Voice,
            request.Instrumental,
            request.Tonal,
            NormalizeTimbre(request.Timbre),
            request.TimbreBright ?? ExtraNumber(request, "timbre_bright"),
            request.Approachability,
            request.Engagement,
            Mood(request, "happy", request.MoodHappy),
            Mood(request, "sad", request.MoodSad),
            Mood(request, "aggressive", request.MoodAggressive),
            Mood(request, "relaxed", request.MoodRelaxed),
            Mood(request, "party", request.MoodParty),
            ParseLabels(genres, AudioLabelKind.Genre),
            ParseLabels(request.Themes, AudioLabelKind.Theme),
            ParseLabels(request.Instruments, AudioLabelKind.Instrument),
            JsonSerializer.Serialize(request, Json));
    }

    public static void Apply(TrackAudioProfile profile, ParsedAudioProfile parsed)
    {
        profile.AnalyzedAt = DateTimeOffset.UtcNow;
        profile.Bpm = parsed.Bpm;
        profile.Key = parsed.Key;
        profile.Scale = parsed.Scale;
        profile.KeyStrength = parsed.KeyStrength;
        profile.Loudness = parsed.Loudness;
        profile.Danceability = parsed.Danceability;
        profile.Acoustic = parsed.Acoustic;
        profile.Electronic = parsed.Electronic;
        profile.Voice = parsed.Voice;
        profile.Instrumental = parsed.Instrumental;
        profile.Tonal = parsed.Tonal;
        profile.Timbre = parsed.Timbre;
        profile.TimbreBright = parsed.TimbreBright;
        profile.Approachability = parsed.Approachability;
        profile.Engagement = parsed.Engagement;
        profile.MoodHappy = parsed.MoodHappy;
        profile.MoodSad = parsed.MoodSad;
        profile.MoodAggressive = parsed.MoodAggressive;
        profile.MoodRelaxed = parsed.MoodRelaxed;
        profile.MoodParty = parsed.MoodParty;
        profile.RawJson = parsed.RawJson;
        profile.Labels.Clear();
        foreach (var label in parsed.Genres.Concat(parsed.Themes).Concat(parsed.Instruments))
        {
            profile.Labels.Add(new TrackAudioLabel
            {
                Kind = label.Kind,
                Name = label.Name,
                Score = label.Score
            });
        }
    }

    public static (string? Key, string? Scale) SplitKey(string? key, string? scale)
    {
        var trimmedKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        var trimmedScale = string.IsNullOrWhiteSpace(scale) ? null : scale.Trim().ToLowerInvariant();
        if (trimmedKey is null)
        {
            return (null, trimmedScale);
        }

        var parts = trimmedKey.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && IsScale(parts[1]))
        {
            return (parts[0], parts[1].ToLowerInvariant());
        }

        return (trimmedKey, trimmedScale);
    }

    public static string? KeyDisplay(string? key, string? scale)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(scale) ? key : $"{key} {scale}";
    }

    public static string? TopMood(TrackAudioProfile profile)
    {
        var moods = new (string Name, double? Score)[]
        {
            ("happy", profile.MoodHappy),
            ("sad", profile.MoodSad),
            ("aggressive", profile.MoodAggressive),
            ("relaxed", profile.MoodRelaxed),
            ("party", profile.MoodParty)
        };
        return moods
            .Where(m => m.Score is not null)
            .OrderByDescending(m => m.Score)
            .Select(m => m.Name)
            .FirstOrDefault();
    }

    public static string FormatSummary(
        TrackAudioProfile profile,
        IReadOnlyList<AudioLabelView>? themes = null,
        IReadOnlyList<AudioLabelView>? instruments = null)
    {
        var parts = new List<string>();
        AddScore(parts, "Dance", profile.Danceability);
        AddScore(parts, "Voice", profile.Voice);
        AddScore(parts, "Acoustic", profile.Acoustic);
        AddScore(parts, "Electronic", profile.Electronic);
        if (!string.IsNullOrWhiteSpace(profile.Timbre))
        {
            parts.Add(char.ToUpperInvariant(profile.Timbre[0]) + profile.Timbre[1..]);
        }

        AddScore(parts, "Approach", profile.Approachability);
        AddScore(parts, "Engage", profile.Engagement);
        if (profile.Bpm is double bpm)
        {
            parts.Add($"{bpm.ToString("0", CultureInfo.InvariantCulture)} BPM");
        }

        var key = KeyDisplay(profile.Key, profile.Scale);
        if (!string.IsNullOrWhiteSpace(key))
        {
            parts.Add(key);
        }

        var line = string.Join(" · ", parts);
        var extra = new List<string>();
        var themeNames = string.Join(", ", (themes ?? []).Select(t => t.Name));
        var instrumentNames = string.Join(", ", (instruments ?? []).Select(t => t.Name));
        if (themeNames.Length > 0)
        {
            extra.Add("Themes: " + themeNames);
        }

        if (instrumentNames.Length > 0)
        {
            extra.Add("Instruments: " + instrumentNames);
        }

        return extra.Count == 0 ? line : line + "\n" + string.Join("\n", extra);
    }

    private static void AddScore(List<string> parts, string label, double? value)
    {
        if (value is double v)
        {
            parts.Add($"{label} {v.ToString("0.00", CultureInfo.InvariantCulture)}");
        }
    }

    private static bool IsScale(string value) =>
        value.Equals("major", StringComparison.OrdinalIgnoreCase)
        || value.Equals("minor", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeTimbre(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed is "dark" or "bright" ? trimmed : trimmed;
    }

    private static double? Mood(AudioProfileWriteRequest request, string name, double? explicitValue)
    {
        if (explicitValue is not null)
        {
            return explicitValue;
        }

        if (request.Moods is null)
        {
            return null;
        }

        foreach (var pair in request.Moods)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static JsonElement? ExtraJson(AudioProfileWriteRequest request, string name)
    {
        if (request.Extra is null)
        {
            return null;
        }

        foreach (var pair in request.Extra)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
                && pair.Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static double? ExtraNumber(AudioProfileWriteRequest request, string name)
    {
        var json = ExtraJson(request, name);
        return json is null ? null : ReadNumber(json.Value);
    }

    public static IReadOnlyList<ParsedAudioLabel> ParseLabels(JsonElement? element, AudioLabelKind kind)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        var json = element.Value;
        if (json.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var labels = new List<ParsedAudioLabel>();
        foreach (var item in json.EnumerateArray())
        {
            if (!TryParseLabel(item, kind, out var label))
            {
                continue;
            }

            labels.Add(label);
        }

        return labels
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Score ?? 0).First())
            .ToList();
    }

    private static bool TryParseLabel(JsonElement item, AudioLabelKind kind, out ParsedAudioLabel label)
    {
        label = default!;
        if (item.ValueKind == JsonValueKind.String)
        {
            var name = PrettyGenre(item.GetString());
            if (name is null)
            {
                return false;
            }

            label = new ParsedAudioLabel(kind, name, null);
            return true;
        }

        if (item.ValueKind == JsonValueKind.Array)
        {
            var values = item.EnumerateArray().ToList();
            if (values.Count == 0 || values[0].ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var name = PrettyGenre(values[0].GetString());
            if (name is null)
            {
                return false;
            }

            double? score = values.Count > 1 ? ReadNumber(values[1]) : null;
            label = new ParsedAudioLabel(kind, name, score);
            return true;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var objectName = PrettyGenre(
            item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null);
        if (objectName is null)
        {
            return false;
        }

        double? objectScore = null;
        if (item.TryGetProperty("score", out var scoreProp))
        {
            objectScore = ReadNumber(scoreProp);
        }

        label = new ParsedAudioLabel(kind, objectName, objectScore);
        return true;
    }

    public static string? PrettyGenre(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace("---", "/", StringComparison.Ordinal);
    }

    private static double? ReadNumber(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
}

public sealed record AudioProfileWriteRequest
{
    public double? Bpm { get; init; }
    public string? Key { get; init; }
    public string? Scale { get; init; }
    public double? KeyStrength { get; init; }
    public double? Loudness { get; init; }
    public double? Danceability { get; init; }
    public double? Acoustic { get; init; }
    public double? Electronic { get; init; }
    public double? Voice { get; init; }
    public double? Instrumental { get; init; }
    public double? Tonal { get; init; }
    public string? Timbre { get; init; }
    public double? TimbreBright { get; init; }
    public double? Approachability { get; init; }
    public double? Engagement { get; init; }
    public double? MoodHappy { get; init; }
    public double? MoodSad { get; init; }
    public double? MoodAggressive { get; init; }
    public double? MoodRelaxed { get; init; }
    public double? MoodParty { get; init; }
    public Dictionary<string, double>? Moods { get; init; }
    public JsonElement? Genres { get; init; }
    public JsonElement? Themes { get; init; }
    public JsonElement? Instruments { get; init; }
    public string? Artist { get; init; }
    public string? Title { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record ParsedAudioProfile(
    double? Bpm,
    string? Key,
    string? Scale,
    double? KeyStrength,
    double? Loudness,
    double? Danceability,
    double? Acoustic,
    double? Electronic,
    double? Voice,
    double? Instrumental,
    double? Tonal,
    string? Timbre,
    double? TimbreBright,
    double? Approachability,
    double? Engagement,
    double? MoodHappy,
    double? MoodSad,
    double? MoodAggressive,
    double? MoodRelaxed,
    double? MoodParty,
    IReadOnlyList<ParsedAudioLabel> Genres,
    IReadOnlyList<ParsedAudioLabel> Themes,
    IReadOnlyList<ParsedAudioLabel> Instruments,
    string RawJson);

public sealed record ParsedAudioLabel(AudioLabelKind Kind, string Name, double? Score);

public sealed record PendingAudioTrack(
    long Id,
    string Artist,
    string Title,
    string? Album,
    string? Mbid,
    int PlayCount);

public sealed record AudioLabelView(string Name, double? Score);

public sealed record AudioProfileView(
    DateTimeOffset AnalyzedAt,
    double? Bpm,
    string? Key,
    string? Scale,
    string? KeyDisplay,
    double? KeyStrength,
    double? Loudness,
    double? Danceability,
    double? Acoustic,
    double? Electronic,
    double? Voice,
    double? Instrumental,
    double? Tonal,
    string? Timbre,
    double? TimbreBright,
    double? Approachability,
    double? Engagement,
    double? MoodHappy,
    double? MoodSad,
    double? MoodAggressive,
    double? MoodRelaxed,
    double? MoodParty,
    string? TopMood,
    IReadOnlyList<AudioLabelView> Genres,
    IReadOnlyList<AudioLabelView> Themes,
    IReadOnlyList<AudioLabelView> Instruments,
    string Summary,
    string? RawJson);

public sealed record AudioAnalyticsResult(
    int ProfiledPlays,
    int UnprofiledPlays,
    int ProfiledTracks,
    int UnprofiledTracks,
    double PercentProfiledPlays,
    double? MeanBpm,
    double? MeanDanceability,
    double? MeanVoice,
    double? MeanAcoustic,
    double? MeanElectronic,
    double? MeanApproachability,
    double? MeanEngagement,
    string? TopMood,
    IReadOnlyList<NamedAverage> MoodAverages,
    IReadOnlyList<NamedCount> BpmBuckets,
    IReadOnlyList<NamedCount> Keys,
    IReadOnlyList<TagStat> Genres,
    IReadOnlyList<AudioGenreFolder> GenreFolders,
    IReadOnlyList<TagStat> Themes,
    IReadOnlyList<TagStat> Instruments);

public sealed record AudioGenreFolder(
    string Name,
    string? Parent,
    string Path,
    int Plays,
    int TrackCount,
    long DurationMs,
    IReadOnlyList<AudioGenreFolder> Children);

public sealed record AudioLabelDetailResult(
    string Kind,
    string Name,
    string Path,
    string? Parent,
    bool IsFolder,
    IReadOnlyList<AudioGenreFolder> Children,
    IReadOnlyList<RankedItem> Tracks,
    IReadOnlyList<RankedItem> Artists);

public sealed record NamedAverage(string Name, double Value);

public sealed record NamedCount(string Name, int Count, int Plays);
