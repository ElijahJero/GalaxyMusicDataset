using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using GalaxyMusicDataset.Services.Http;

namespace GalaxyMusicDataset.Services.Vgmdb;

public sealed record VgmdbAlbumHit(
    string Id,
    string? Catalog,
    string? Category,
    string? ReleaseDate,
    string? MediaFormat,
    IReadOnlyList<string> Titles,
    string RawJson);

public sealed record VgmdbDiscTrack(
    IReadOnlyList<string> Names,
    string? LengthRaw,
    int? DurationMs);

public sealed record VgmdbDisc(
    string? Name,
    IReadOnlyList<VgmdbDiscTrack> Tracks);

public sealed record VgmdbAlbum(
    string Id,
    string? Name,
    IReadOnlyList<string> Names,
    string? Catalog,
    string? Classification,
    string? ReleaseDate,
    int? ReleaseYear,
    string? CoverUrl,
    string? Category,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> ProductNames,
    IReadOnlyList<VgmdbDisc> Discs,
    string RawJson);

public sealed class VgmdbClient(HttpClient http, ApiCallRecorder recorder)
{
    public static readonly ApiRateLimiter RateLimiter = new(TimeSpan.FromMilliseconds(1100));

    public static SocketsHttpHandler CreateSocketsHandler() =>
        new()
        {
            ConnectCallback = ConnectIPv4PreferredAsync
        };

    /// <summary>
    /// Prefer A records. Dual-stack stacks often fail with "No route to host"
    /// when a useless AAAA is tried first.
    /// </summary>
    internal static async ValueTask<Stream> ConnectIPv4PreferredAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);
        if (addresses.Length == 0)
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                socket.Dispose();
            }
        }

        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }

    private static readonly Regex YearRegex = new(@"\b((?:19|20)\d{2})\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public required string BaseUrl { get; init; }
    public required string UserAgent { get; init; }

    public async Task<(IReadOnlyList<VgmdbAlbumHit> Items, string RawJson)> SearchAlbumsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildAlbumSearchUrl(BaseUrl, query));
        ApplyHeaders(request);
        var json = await recorder.SendAsync(http, request, "VGMdb", RateLimiter, cancellationToken, maxAttempts: 2);
        return ParseSearch(json);
    }

    public async Task<VgmdbAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildAlbumUrl(BaseUrl, albumId));
        ApplyHeaders(request);
        var json = await recorder.SendAsync(http, request, "VGMdb", RateLimiter, cancellationToken, maxAttempts: 2);
        return ParseAlbum(json, albumId);
    }

    public static string BuildAlbumSearchUrl(string baseUrl, string query)
    {
        var root = baseUrl.TrimEnd('/');
        return $"{root}/search/albums?q={Uri.EscapeDataString(query)}&format=json";
    }

    public static string BuildAlbumUrl(string baseUrl, string albumId)
    {
        var root = baseUrl.TrimEnd('/');
        return $"{root}/album/{Uri.EscapeDataString(albumId)}?format=json";
    }

    public static string? AlbumIdFromLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        var trimmed = link.Trim().Trim('/');
        var slash = trimmed.LastIndexOf('/');
        var id = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    public static (IReadOnlyList<VgmdbAlbumHit> Items, string RawJson) ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!TryGetAlbumsArray(root, out var albums))
        {
            return ([], json);
        }

        var list = new List<VgmdbAlbumHit>();
        foreach (var item in albums.EnumerateArray())
        {
            var id = AlbumIdFromLink(item.GetPropertyString("link"));
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var titles = ReadNameMap(item, "titles", "names");
            var name = item.GetPropertyString("name");
            if (!string.IsNullOrWhiteSpace(name) && !titles.Contains(name, StringComparer.Ordinal))
            {
                titles = [.. titles, name];
            }

            list.Add(new VgmdbAlbumHit(
                id,
                NullIfBlank(item.GetPropertyString("catalog")),
                NullIfBlank(item.GetPropertyString("category")),
                NullIfBlank(item.GetPropertyString("release_date")),
                NullIfBlank(item.GetPropertyString("media_format")),
                titles,
                item.GetRawText()));
        }

        return (list, json);
    }

    public static VgmdbAlbum? ParseAlbum(string json, string? idHint = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = AlbumIdFromLink(root.GetPropertyString("link")) ?? NullIfBlank(idHint) ?? "";
        var names = ReadNameMap(root, "names", "titles");
        var name = root.GetPropertyString("name") ?? names.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.Ordinal))
        {
            names = [name, .. names];
        }

        var categories = ReadStringList(root, "categories");
        var category = NullIfBlank(root.GetPropertyString("category"));
        if (!string.IsNullOrWhiteSpace(category) &&
            !categories.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            categories = [category, .. categories];
        }

        var cover = FirstCoverUrl(root);
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name) && cover is null)
        {
            return null;
        }
        var releaseDate = NullIfBlank(root.GetPropertyString("release_date"));
        return new VgmdbAlbum(
            id,
            name,
            names,
            NullIfBlank(root.GetPropertyString("catalog")),
            NullIfBlank(root.GetPropertyString("classification")),
            releaseDate,
            ParseYear(releaseDate),
            cover,
            category,
            categories,
            ReadStringList(root, "platforms"),
            ReadProductNames(root),
            ReadDiscs(root),
            json);
    }

    public static IReadOnlyList<(string Name, int Weight)> TagPairs(VgmdbAlbum album)
    {
        var tags = new List<(string Name, int Weight)>();
        foreach (var category in album.Categories)
        {
            AddTag(tags, category, 50);
        }

        foreach (var piece in SplitClassification(album.Classification))
        {
            AddTag(tags, piece, 50);
        }

        foreach (var platform in album.Platforms)
        {
            AddTag(tags, platform, 40);
        }

        foreach (var product in album.ProductNames)
        {
            AddTag(tags, product, 45);
        }

        return tags;
    }

    public static int? ParseDurationMs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = raw.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var values = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])
                || values[i] < 0)
            {
                return null;
            }
        }

        var seconds = parts.Length == 2
            ? (values[0] * 60) + values[1]
            : (values[0] * 3600) + (values[1] * 60) + values[2];
        return seconds <= 0 ? null : seconds * 1000;
    }

    public static int? ParseYear(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            return null;
        }

        var match = YearRegex.Match(releaseDate);
        return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : null;
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static bool TryGetAlbumsArray(JsonElement root, out JsonElement albums)
    {
        albums = default;
        if (root.TryGetProperty("results", out var results))
        {
            if (results.ValueKind == JsonValueKind.Object &&
                results.TryGetProperty("albums", out albums) &&
                albums.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            if (results.ValueKind == JsonValueKind.Array)
            {
                albums = results;
                return true;
            }
        }

        if (root.TryGetProperty("albums", out albums) && albums.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ReadNameMap(JsonElement element, params string[] propertyNames)
    {
        var names = new List<string>();
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                AddName(names, value.GetString());
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                {
                    AddName(names, property.Value.GetFlexibleText());
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    AddName(names, item.GetFlexibleText());
                }
            }
        }

        return names;
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return SplitClassification(value.GetString()).ToList();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            AddName(list, item.GetFlexibleText());
        }

        return list;
    }

    private static IReadOnlyList<string> ReadProductNames(JsonElement root)
    {
        if (!root.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var product in products.EnumerateArray())
        {
            foreach (var name in ReadNameMap(product, "names", "titles"))
            {
                AddName(names, name);
            }

            AddName(names, product.GetPropertyString("name"));
        }

        return names;
    }

    private static IReadOnlyList<VgmdbDisc> ReadDiscs(JsonElement root)
    {
        if (!root.TryGetProperty("discs", out var discs) || discs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<VgmdbDisc>();
        foreach (var disc in discs.EnumerateArray())
        {
            var tracks = new List<VgmdbDiscTrack>();
            if (disc.TryGetProperty("tracks", out var trackEl) && trackEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var track in trackEl.EnumerateArray())
                {
                    var names = ReadNameMap(track, "names", "titles").ToList();
                    AddName(names, track.GetPropertyString("name"));
                    var length = NullIfBlank(track.GetPropertyString("track_length"));
                    tracks.Add(new VgmdbDiscTrack(names, length, ParseDurationMs(length)));
                }
            }

            list.Add(new VgmdbDisc(NullIfBlank(disc.GetPropertyString("name")), tracks));
        }

        return list;
    }

    private static string? FirstCoverUrl(JsonElement root)
    {
        foreach (var key in new[] { "picture_small", "picture_full", "picture_thumb" })
        {
            var url = NullIfBlank(root.GetPropertyString(key));
            if (url is not null)
            {
                return url;
            }
        }

        if (!root.TryGetProperty("covers", out var covers) || covers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var cover in covers.EnumerateArray())
        {
            foreach (var key in new[] { "medium", "full", "thumb" })
            {
                var url = NullIfBlank(cover.GetPropertyString(key));
                if (url is not null)
                {
                    return url;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitClassification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var piece in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            yield return piece;
        }
    }

    private static void AddTag(List<(string Name, int Weight)> tags, string? name, int weight)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        tags.Add((name.Trim(), weight));
    }

    private static void AddName(List<string> names, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            names.Contains(value, StringComparer.Ordinal))
        {
            return;
        }

        names.Add(value.Trim());
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
