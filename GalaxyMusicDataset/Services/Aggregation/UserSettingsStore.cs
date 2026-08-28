using System.Text.Json;
using GalaxyMusicDataset.Configuration;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class UserSettingsStore(IWebHostEnvironment env, IConfiguration configuration)
{
    private readonly string _path = Path.Combine(env.ContentRootPath, "App_Data", "user-settings.json");

    public async Task SaveAsync(UserSettingsModel model, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var payload = new Dictionary<string, object?>
        {
            ["LastFm"] = new Dictionary<string, string?>
            {
                ["ApiKey"] = model.LastFmApiKey,
                ["Username"] = model.LastFmUsername
            },
            ["Discogs"] = new Dictionary<string, string?>
            {
                ["Token"] = model.DiscogsToken
            },
            ["TheAudioDb"] = new Dictionary<string, string?>
            {
                ["ApiKey"] = model.TheAudioDbApiKey
            },
            ["MusicBrainz"] = new Dictionary<string, string?>
            {
                ["Contact"] = model.MusicBrainzContact
            },
            ["Aggregation"] = new Dictionary<string, object?>
            {
                ["EnableMusicBrainz"] = model.EnableMusicBrainz,
                ["EnableLastFmTrackInfo"] = model.EnableLastFmTrackInfo,
                ["EnableDiscogs"] = model.EnableDiscogs,
                ["EnableTheAudioDb"] = model.EnableTheAudioDb,
                ["IncrementalIntervalMinutes"] = model.IncrementalIntervalMinutes,
                ["SeedSampleData"] = model.SeedSampleData
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json, cancellationToken);
        if (configuration is IConfigurationRoot root)
        {
            root.Reload();
        }
    }
}

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
