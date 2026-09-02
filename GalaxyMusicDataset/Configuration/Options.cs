namespace GalaxyMusicDataset.Configuration;

public sealed class LastFmOptions
{
    public const string SectionName = "LastFm";

    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string UserAgent { get; set; } = "GalaxyMusicDataset/0.1 (https://github.com/elijahjero/galaxymusicdataset)";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Username);
}

public sealed class MusicBrainzOptions
{
    public const string SectionName = "MusicBrainz";
    public const string DefaultBaseUrl = "https://musicbrainz.org";
    public const string DefaultCoverArtBaseUrl = "https://coverartarchive.org";
    public const int PublicMinIntervalMs = 1200;
    public const int LocalMinIntervalMs = 50;

    public string UserAgent { get; set; } = "GalaxyMusicDataset/0.1 (https://github.com/elijahjero/galaxymusicdataset)";
    public string Contact { get; set; } = "https://github.com/elijahjero/galaxymusicdataset";

    /// <summary>
    /// MusicBrainz Server origin (website + <c>/ws/2</c>). Default is the public API.
    /// Point this at a self-hosted mirror such as
    /// <see href="https://github.com/metabrainz/musicbrainz-docker">musicbrainz-docker</see>
    /// (<c>http://localhost:5000</c>) — this app does not run Docker for you.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Cover Art Archive origin. Default is the public CAA. Only change this if you
    /// also host a CAA mirror; musicbrainz-docker's website on :5000 is not CAA.
    /// </summary>
    public string? CoverArtBaseUrl { get; set; }

    /// <summary>
    /// Minimum milliseconds between MusicBrainz Web Service calls.
    /// Null = auto (1200ms on musicbrainz.org, 50ms on a self-hosted mirror).
    /// 0 = no extra delay. Public Cover Art Archive stays at 1200ms regardless.
    /// </summary>
    public int? MinIntervalMs { get; set; }

    public string ResolvedBaseUrl => MusicBrainzEndpoints.NormalizeBaseUrl(BaseUrl, DefaultBaseUrl);

    public string ResolvedCoverArtBaseUrl =>
        MusicBrainzEndpoints.NormalizeBaseUrl(CoverArtBaseUrl, DefaultCoverArtBaseUrl);

    public string WebServiceRoot => MusicBrainzEndpoints.WebServiceRoot(ResolvedBaseUrl);

    public bool UsesPublicWebService => MusicBrainzEndpoints.IsPublicMusicBrainzHost(ResolvedBaseUrl);

    public bool UsesPublicCoverArt => MusicBrainzEndpoints.IsPublicCoverArtHost(ResolvedCoverArtBaseUrl);

    public TimeSpan WebServiceMinInterval
    {
        get
        {
            var ms = MinIntervalMs ?? (UsesPublicWebService ? PublicMinIntervalMs : LocalMinIntervalMs);
            return TimeSpan.FromMilliseconds(Math.Max(0, ms));
        }
    }

    public TimeSpan CoverArtMinInterval =>
        UsesPublicCoverArt
            ? TimeSpan.FromMilliseconds(PublicMinIntervalMs)
            : TimeSpan.FromMilliseconds(Math.Max(0, MinIntervalMs ?? LocalMinIntervalMs));

    public string RecordingSearchUrl(string query) =>
        $"{WebServiceRoot}/recording/?query={Uri.EscapeDataString(query)}&fmt=json&limit=8";

    public string RecordingLookupUrl(string mbid) =>
        $"{WebServiceRoot}/recording/{Uri.EscapeDataString(mbid)}?inc=artist-credits+releases+aliases+tags+isrcs+genres&fmt=json";

    public string ArtistLookupUrl(string mbid) =>
        $"{WebServiceRoot}/artist/{Uri.EscapeDataString(mbid)}?inc=aliases&fmt=json";

    public string ReleaseCoverArtUrl(string releaseMbid) =>
        $"{ResolvedCoverArtBaseUrl}/release/{Uri.EscapeDataString(releaseMbid)}";
}

public static class MusicBrainzEndpoints
{
    public static string NormalizeBaseUrl(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback.TrimEnd('/');
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "http://" + trimmed;
        }

        return trimmed.TrimEnd('/');
    }

    public static string WebServiceRoot(string? baseUrl)
    {
        var root = NormalizeBaseUrl(baseUrl, MusicBrainzOptions.DefaultBaseUrl);
        if (root.EndsWith("/ws/2", StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return root + "/ws/2";
    }

    public static bool IsPublicMusicBrainzHost(string? baseUrl)
    {
        if (!TryGetHost(NormalizeBaseUrl(baseUrl, MusicBrainzOptions.DefaultBaseUrl), out var host))
        {
            return true;
        }

        return host.Equals("musicbrainz.org", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".musicbrainz.org", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPublicCoverArtHost(string? baseUrl)
    {
        if (!TryGetHost(NormalizeBaseUrl(baseUrl, MusicBrainzOptions.DefaultCoverArtBaseUrl), out var host))
        {
            return true;
        }

        return host.Equals("coverartarchive.org", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".coverartarchive.org", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHost(string url, out string host)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
            return true;
        }

        host = "";
        return false;
    }
}

public sealed class DiscogsOptions
{
    public const string SectionName = "Discogs";

    public string? Token { get; set; }
    public string UserAgent { get; set; } = "GalaxyMusicDataset/0.1 +https://github.com/elijahjero/galaxymusicdataset";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);
}

public sealed class TheAudioDbOptions
{
    public const string SectionName = "TheAudioDb";

    public string? ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public abstract class VocaDbSiteOptions
{
    public const int DefaultMinIntervalMs = 500;
    public const string DefaultUserAgent = "GalaxyMusicDataset/0.1 (https://github.com/elijahjero/galaxymusicdataset)";

    public string? BaseUrl { get; set; }
    public string UserAgent { get; set; } = DefaultUserAgent;

    public abstract string DefaultBaseUrl { get; }

    public string ResolvedBaseUrl => MusicBrainzEndpoints.NormalizeBaseUrl(BaseUrl, DefaultBaseUrl);

    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(DefaultMinIntervalMs);
}

public sealed class VocaDbOptions : VocaDbSiteOptions
{
    public const string SectionName = "VocaDb";
    public override string DefaultBaseUrl => "https://vocadb.net";
}

public sealed class UtaiteDbOptions : VocaDbSiteOptions
{
    public const string SectionName = "UtaiteDb";
    public override string DefaultBaseUrl => "https://utaitedb.net";
}

public sealed class TouhouDbOptions : VocaDbSiteOptions
{
    public const string SectionName = "TouhouDb";
    public override string DefaultBaseUrl => "https://touhoudb.com";
}

public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    public int IncrementalIntervalMinutes { get; set; } = 60;
    public int OverlapSeconds { get; set; } = 300;
    public int LastFmPageSize { get; set; } = 200;
    public bool SeedSampleData { get; set; }
    public bool EnableMusicBrainz { get; set; } = true;
    public bool EnableLastFmTrackInfo { get; set; } = true;
    public bool EnableDiscogs { get; set; } = true;
    public bool EnableTheAudioDb { get; set; } = true;
    public bool EnableVocaDb { get; set; } = true;
    public bool EnableUtaiteDb { get; set; } = true;
    public bool EnableTouhouDb { get; set; } = true;
    public double AutoMatchThreshold { get; set; } = 0.92;
    public double ReviewThreshold { get; set; } = 0.55;
    public int ApiLogRetention { get; set; } = 500;
}
