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
        var discogs = KeepIfBlank(posted.DiscogsToken, existing.DiscogsToken);
        var audioDb = KeepIfBlank(posted.TheAudioDbApiKey, existing.TheAudioDbApiKey);
        var payload = new Dictionary<string, object?>
        {
            ["LastFm"] = new Dictionary<string, string?>
            {
                ["ApiKey"] = lastFmKey,
                ["Username"] = posted.LastFmUsername
            },
            ["Discogs"] = new Dictionary<string, string?>
            {
                ["Token"] = discogs
            },
            ["TheAudioDb"] = new Dictionary<string, string?>
            {
                ["ApiKey"] = audioDb
            },
            ["MusicBrainz"] = new Dictionary<string, string?>
            {
                ["Contact"] = posted.MusicBrainzContact
            },
            ["Aggregation"] = new Dictionary<string, object?>
            {
                ["EnableMusicBrainz"] = posted.EnableMusicBrainz,
                ["EnableLastFmTrackInfo"] = posted.EnableLastFmTrackInfo,
                ["EnableDiscogs"] = posted.EnableDiscogs,
                ["EnableTheAudioDb"] = posted.EnableTheAudioDb,
                ["IncrementalIntervalMinutes"] = posted.IncrementalIntervalMinutes,
                ["SeedSampleData"] = posted.SeedSampleData
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
}

public readonly record struct StoredSecrets(string? LastFmApiKey, string? DiscogsToken, string? TheAudioDbApiKey);

public sealed class UserSettingsModel
{
    public string? LastFmApiKey { get; set; }
    public string? LastFmUsername { get; set; }
    public string? DiscogsToken { get; set; }
    public string? TheAudioDbApiKey { get; set; }
    public string? MusicBrainzContact { get; set; }
    public bool EnableMusicBrainz { get; set; } = true;
    public bool EnableLastFmTrackInfo { get; set; } = true;
    public bool EnableDiscogs { get; set; } = true;
    public bool EnableTheAudioDb { get; set; } = true;
    public int IncrementalIntervalMinutes { get; set; } = 60;
    public bool SeedSampleData { get; set; }
}
