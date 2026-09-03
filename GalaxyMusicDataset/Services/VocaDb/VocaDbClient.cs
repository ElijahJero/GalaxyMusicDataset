using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.VocaDb;

public sealed record VocaDbArtistCredit(string Name, IReadOnlyList<string> AllNames, string? Categories);

public sealed record VocaDbTagHit(string Name, string? Category, int Count);

public sealed record VocaDbSongHit(
    string Id,
    string? Name,
    string? DefaultName,
    string? AdditionalNames,
    string? ArtistString,
    string? SongType,
    int? LengthSeconds,
    string? ThumbUrl,
    string? AlbumTitle,
    int? ReleaseYear,
    string? MusicVideoUrl,
    string? MusicBrainzId,
    IReadOnlyList<string> AllTitles,
    IReadOnlyList<VocaDbArtistCredit> Artists,
    IReadOnlyList<VocaDbTagHit> Tags,
    string RawJson);

public sealed class VocaDbClient(HttpClient http, ApiCallRecorder recorder)
{
    public const string SearchFields =
        "AdditionalNames,Artists,Tags,PVs,MainPicture,Albums,Names,WebLinks,ThumbUrl";

    public static readonly ApiRateLimiter VocaDbLimiter = new(TimeSpan.FromMilliseconds(500));
    public static readonly ApiRateLimiter UtaiteDbLimiter = new(TimeSpan.FromMilliseconds(500));
    public static readonly ApiRateLimiter TouhouDbLimiter = new(TimeSpan.FromMilliseconds(500));

    private static readonly Regex MusicBrainzRecording =
        new(@"musicbrainz\.org/recording/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public required string BaseUrl { get; init; }
    public required string SourceName { get; init; }
    public required string UserAgent { get; init; }
    public required ApiRateLimiter RateLimiter { get; init; }

    public static ApiRateLimiter LimiterFor(EnrichmentSource source) => source switch
    {
        EnrichmentSource.VocaDb => VocaDbLimiter,
        EnrichmentSource.UtaiteDb => UtaiteDbLimiter,
        EnrichmentSource.TouhouDb => TouhouDbLimiter,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not a VocaDB-family source.")
    };

    public async Task<(IReadOnlyList<VocaDbSongHit> Items, string RawJson)> SearchSongsAsync(
        string artist,
        string title,
        CancellationToken cancellationToken)
    {
        _ = artist;
        var root = BaseUrl.TrimEnd('/');
        var url =
            $"{root}/api/songs?query={Uri.EscapeDataString(title)}" +
            $"&fields={SearchFields}" +
            "&nameMatchMode=Auto&preferAccurateMatches=true&maxResults=10&lang=Default";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Fail fast when VocaDB is overloaded; enrichment backs off per-track and per-source.
        var json = await recorder.SendAsync(http, request, SourceName, RateLimiter, cancellationToken, maxAttempts: 2);
        return ParseSearch(json);
    }

    public static (IReadOnlyList<VocaDbSongHit> Items, string RawJson) ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return ([], json);
        }

        var list = new List<VocaDbSongHit>();
        foreach (var item in items.EnumerateArray())
        {
            var hit = ReadSong(item);
            if (hit is not null)
            {
                list.Add(hit);
            }
        }

        return (list, json);
    }

    public static VocaDbSongHit? ParseSong(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("items", out _))
        {
            return ParseSearch(json).Items.FirstOrDefault();
        }

        return ReadSong(root);
    }

    public static VocaDbSongHit? ReadSong(JsonElement root)
    {
        var id = root.GetPropertyString("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var name = EmptyToNull(root.GetPropertyString("name"));
        var defaultName = EmptyToNull(root.GetPropertyString("defaultName"));
        var additionalNames = EmptyToNull(root.GetPropertyString("additionalNames"));
        var titles = new List<string>();
        AddName(titles, name);
        AddName(titles, defaultName);
        foreach (var extra in SplitNames(additionalNames))
        {
            AddName(titles, extra);
        }

        if (root.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in names.EnumerateArray())
            {
                AddName(titles, EmptyToNull(item.GetPropertyString("value")));
            }
        }

        var artists = ReadArtists(root);
        var tags = ReadTags(root);
        var publishYear = YearFromDate(root.GetPropertyString("publishDate"));
        return new VocaDbSongHit(
            id,
            name,
            defaultName,
            additionalNames,
            EmptyToNull(root.GetPropertyString("artistString")),
            EmptyToNull(root.GetPropertyString("songType")),
            root.GetPropertyInt("lengthSeconds"),
            CoverUrl(root),
            FirstAlbumTitle(root),
            publishYear,
            FirstVideoUrl(root),
            MusicBrainzRecordingId(root),
            titles,
            artists,
            tags,
            root.GetRawText());
    }

    public static IReadOnlyList<(string Name, int Weight)> TagPairs(VocaDbSongHit hit)
    {
        var pairs = new List<(string, int)>();
        foreach (var tag in hit.Tags)
        {
            var weight = string.Equals(tag.Category, "Genres", StringComparison.OrdinalIgnoreCase)
                ? 80
                : Math.Max(1, tag.Count);
            pairs.Add((tag.Name, weight));
        }

        return pairs;
    }

    public static string? LocaleForLanguage(string? language) => language switch
    {
        "Japanese" => "ja",
        "English" => "en",
        "Romaji" => "romaji",
        _ => null
    };

    private static IReadOnlyList<VocaDbArtistCredit> ReadArtists(JsonElement root)
    {
        if (!root.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<VocaDbArtistCredit>();
        foreach (var item in artists.EnumerateArray())
        {
            var display = EmptyToNull(item.GetPropertyString("name"));
            string? additional = null;
            string? nestedName = null;
            if (item.TryGetProperty("artist", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                nestedName = EmptyToNull(nested.GetPropertyString("name"));
                additional = EmptyToNull(nested.GetPropertyString("additionalNames"));
            }

            var primary = display ?? nestedName;
            if (primary is null)
            {
                continue;
            }

            var names = new List<string>();
            AddName(names, display);
            AddName(names, nestedName);
            foreach (var extra in SplitNames(additional))
            {
                AddName(names, extra);
            }

            list.Add(new VocaDbArtistCredit(
                primary,
                names,
                EmptyToNull(item.GetPropertyString("categories"))));
        }

        return list;
    }

    private static IReadOnlyList<VocaDbTagHit> ReadTags(JsonElement root)
    {
        if (!root.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<VocaDbTagHit>();
        foreach (var item in tags.EnumerateArray())
        {
            string? name = null;
            string? category = null;
            if (item.TryGetProperty("tag", out var tag) && tag.ValueKind == JsonValueKind.Object)
            {
                name = EmptyToNull(tag.GetPropertyString("name"));
                category = EmptyToNull(tag.GetPropertyString("categoryName"));
            }

            name ??= EmptyToNull(item.GetPropertyString("name"));
            if (name is null)
            {
                continue;
            }

            list.Add(new VocaDbTagHit(name, category, item.GetPropertyInt("count") ?? 0));
        }

        return list;
    }

    private static string? CoverUrl(JsonElement root)
    {
        var thumb = EmptyToNull(root.GetPropertyString("thumbUrl"));
        if (thumb is not null)
        {
            return thumb;
        }

        if (!root.TryGetProperty("mainPicture", out var picture) || picture.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return EmptyToNull(picture.GetPropertyString("urlThumb"))
               ?? EmptyToNull(picture.GetPropertyString("urlOriginal"))
               ?? EmptyToNull(picture.GetPropertyString("urlSmallThumb"));
    }

    private static string? FirstAlbumTitle(JsonElement root)
    {
        if (!root.TryGetProperty("albums", out var albums) || albums.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var album in albums.EnumerateArray())
        {
            var title = EmptyToNull(album.GetPropertyString("name"));
            if (title is not null)
            {
                return title;
            }
        }

        return null;
    }

    private static string? FirstVideoUrl(JsonElement root)
    {
        if (!root.TryGetProperty("pvs", out var pvs) || pvs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallback = null;
        foreach (var pv in pvs.EnumerateArray())
        {
            if (pv.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (pv.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var url = EmptyToNull(pv.GetPropertyString("url"));
            if (url is null)
            {
                continue;
            }

            var type = pv.GetPropertyString("pvType");
            var service = pv.GetPropertyString("service");
            var original = string.Equals(type, "Original", StringComparison.OrdinalIgnoreCase);
            if (original && string.Equals(service, "Youtube", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (original && fallback is null)
            {
                fallback = url;
            }
            else
            {
                fallback ??= url;
            }
        }

        return fallback;
    }

    private static string? MusicBrainzRecordingId(JsonElement root)
    {
        if (!root.TryGetProperty("webLinks", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var link in links.EnumerateArray())
        {
            var url = link.GetPropertyString("url");
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var match = MusicBrainzRecording.Match(url);
            if (match.Success)
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    private static int? YearFromDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
        {
            return null;
        }

        return int.TryParse(value[..4], out var year) && year > 0 ? year : null;
    }

    private static IEnumerable<string> SplitNames(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            yield return part;
        }
    }

    private static void AddName(List<string> names, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (names.Any(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        names.Add(value);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
