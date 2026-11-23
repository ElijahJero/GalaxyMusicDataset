using System.Text.Json;
using GalaxyMusicDataset.Configuration;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class UserSettingsStore(IWebHostEnvironment env, IConfiguration configuration)
{
    private readonly string _path = Path.Combine(env.ContentRootPath, "App_Data", "user-settings.json");

    public async Task SaveAsync(UserSettingsModel posted, StoredSecrets existing, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var lastFmKey = KeepIfBlank(posted.LastFmApiKey, existing.LastFmApiKey);
        var libreFmKey = KeepIfBlank(posted.LibreFmApiKey, existing.LibreFmApiKey);
        var malojaKey = KeepIfBlank(posted.MalojaApiKey, existing.MalojaApiKey);
        var listenBrainzToken = KeepIfBlank(posted.ListenBrainzToken, existing.ListenBrainzToken);
        var discogs = KeepIfBlank(posted.DiscogsToken, existing.DiscogsToken);
        var audioDb = KeepIfBlank(posted.TheAudioDbApiKey, existing.TheAudioDbApiKey);
        var payload = new Dictionary<string, object?>
        {
            ["LastFm"] = new Dictionary<string, string?>
            {
                ["ApiKey"] = lastFmKey,
                ["Username"] = posted.LastFmUsername
            },
            ["LibreFm"] = new Dictionary<string, string?>
            {
                ["ApiKey"] = libreFmKey,
                ["Username"] = posted.LibreFmUsername,
                ["BaseUrl"] = string.IsNullOrWhiteSpace(posted.LibreFmBaseUrl)
                    ? ""
                    : posted.LibreFmBaseUrl.Trim()
            },
            ["Maloja"] = new Dictionary<string, object?>
            {
                ["BaseUrl"] = string.IsNullOrWhiteSpace(posted.MalojaBaseUrl)
                    ? ""
                    : posted.MalojaBaseUrl.Trim(),
                ["ApiKey"] = malojaKey,
                ["MinIntervalMs"] = posted.MalojaMinIntervalMs
            },
            ["ListenBrainz"] = new Dictionary<string, string?>
            {
                ["Username"] = posted.ListenBrainzUsername,
                ["Token"] = listenBrainzToken,
                ["BaseUrl"] = string.IsNullOrWhiteSpace(posted.ListenBrainzBaseUrl)
                    ? ""
                    : posted.ListenBrainzBaseUrl.Trim()
            },
            ["Discogs"] = new Dictionary<string, string?>
            {
                ["Token"] = discogs
            },
            ["TheAudioDb"] = new Dictionary<string, string?>
            {
                ["ApiKey"] = audioDb
            },
            ["MusicBrainz"] = BuildMusicBrainzSection(posted),
            ["Vgmdb"] = new Dictionary<string, object?>
            {
                ["BaseUrl"] = string.IsNullOrWhiteSpace(posted.VgmdbBaseUrl)
                    ? ""
                    : posted.VgmdbBaseUrl.Trim()
            },
            ["Aggregation"] = new Dictionary<string, object?>
            {
                ["PrimaryScrobbleSource"] = string.IsNullOrWhiteSpace(posted.PrimaryScrobbleSource)
                    ? "LastFm"
                    : posted.PrimaryScrobbleSource.Trim(),
                ["CrossProviderMatchSeconds"] = Math.Max(0, posted.CrossProviderMatchSeconds),
                ["EnableMusicBrainz"] = posted.EnableMusicBrainz,
                ["EnableLastFmTrackInfo"] = posted.EnableLastFmTrackInfo,
                ["EnableMalojaAlbumArt"] = posted.EnableMalojaAlbumArt,
                ["EnableDiscogs"] = posted.EnableDiscogs,
                ["EnableTheAudioDb"] = posted.EnableTheAudioDb,
                ["EnableVocaDb"] = posted.EnableVocaDb,
                ["EnableUtaiteDb"] = posted.EnableUtaiteDb,
                ["EnableTouhouDb"] = posted.EnableTouhouDb,
                ["EnableVgmdb"] = posted.EnableVgmdb,
                ["IncrementalIntervalMinutes"] = posted.IncrementalIntervalMinutes,
                ["SeedSampleData"] = posted.SeedSampleData
            },
            ["Ui"] = new Dictionary<string, object?>
            {
                ["DefaultTheme"] = UiOptions.NormalizeTheme(posted.DefaultTheme)
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json, cancellationToken);
        if (configuration is IConfigurationRoot root)
        {
            root.Reload();
        }
    }

    public static string? KeepIfBlank(string? posted, string? existing) =>
        string.IsNullOrWhiteSpace(posted) ? existing : posted.Trim();

    private static Dictionary<string, object?> BuildMusicBrainzSection(UserSettingsModel posted)
    {
        var mb = new Dictionary<string, object?>
        {
            ["Contact"] = posted.MusicBrainzContact,
            ["BaseUrl"] = string.IsNullOrWhiteSpace(posted.MusicBrainzBaseUrl)
                ? ""
                : posted.MusicBrainzBaseUrl.Trim(),
            ["CoverArtBaseUrl"] = string.IsNullOrWhiteSpace(posted.MusicBrainzCoverArtBaseUrl)
                ? ""
                : posted.MusicBrainzCoverArtBaseUrl.Trim()
        };
        if (posted.MusicBrainzMinIntervalMs is int interval)
        {
            mb["MinIntervalMs"] = Math.Max(0, interval);
        }

        return mb;
    }
}

public readonly record struct StoredSecrets(
    string? LastFmApiKey,
    string? DiscogsToken,
    string? TheAudioDbApiKey,
    string? LibreFmApiKey = null,
    string? MalojaApiKey = null,
    string? ListenBrainzToken = null);

public sealed class UserSettingsModel
{
    public string? PrimaryScrobbleSource { get; set; } = "LastFm";
    public int CrossProviderMatchSeconds { get; set; } = 2;
    public string? LastFmApiKey { get; set; }
    public string? LastFmUsername { get; set; }
    public string? LibreFmApiKey { get; set; }
    public string? LibreFmUsername { get; set; }
    public string? LibreFmBaseUrl { get; set; }
    public string? MalojaBaseUrl { get; set; }
    public string? MalojaApiKey { get; set; }
    public int? MalojaMinIntervalMs { get; set; }
    public string? ListenBrainzUsername { get; set; }
    public string? ListenBrainzToken { get; set; }
    public string? ListenBrainzBaseUrl { get; set; }
    public string? DiscogsToken { get; set; }
    public string? TheAudioDbApiKey { get; set; }
    public string? MusicBrainzContact { get; set; }
    public string? MusicBrainzBaseUrl { get; set; }
    public string? MusicBrainzCoverArtBaseUrl { get; set; }
    public int? MusicBrainzMinIntervalMs { get; set; }
    public bool EnableMusicBrainz { get; set; } = true;
    public bool EnableLastFmTrackInfo { get; set; } = true;
    public bool EnableMalojaAlbumArt { get; set; } = true;
    public bool EnableDiscogs { get; set; } = true;
    public bool EnableTheAudioDb { get; set; } = true;
    public bool EnableVocaDb { get; set; } = true;
    public bool EnableUtaiteDb { get; set; } = true;
    public bool EnableTouhouDb { get; set; } = true;
    public bool EnableVgmdb { get; set; } = true;
    public string? VgmdbBaseUrl { get; set; }
    public int IncrementalIntervalMinutes { get; set; } = 60;
    public bool SeedSampleData { get; set; }

    /// <summary>Server default theme for browsers with no local preference: dark, light, or maloja.</summary>
    public string? DefaultTheme { get; set; } = "dark";
}
