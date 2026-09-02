using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Services.Aggregation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Pages;

public class SettingsModel(
    IOptionsMonitor<LastFmOptions> lastFm,
    IOptionsMonitor<DiscogsOptions> discogs,
    IOptionsMonitor<TheAudioDbOptions> audioDb,
    IOptionsMonitor<MusicBrainzOptions> musicBrainz,
    IOptionsMonitor<AggregationOptions> aggregation,
    UserSettingsStore store) : PageModel
{
    [BindProperty]
    public UserSettingsModel Input { get; set; } = new();

    public string? Saved { get; set; }
    public bool LastFmKeySaved { get; set; }
    public bool DiscogsTokenSaved { get; set; }
    public bool TheAudioDbKeySaved { get; set; }

    public void OnGet() => LoadForm();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var existing = new StoredSecrets(
            lastFm.CurrentValue.ApiKey,
            discogs.CurrentValue.Token,
            audioDb.CurrentValue.ApiKey);
        await store.SaveAsync(Input, existing, cancellationToken);
        Saved = "Saved. Blank secret fields were left unchanged.";
        var postedKey = Input.LastFmApiKey;
        var postedDiscogs = Input.DiscogsToken;
        var postedAudioDb = Input.TheAudioDbApiKey;
        LoadForm();
        LastFmKeySaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedKey, existing.LastFmApiKey));
        DiscogsTokenSaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedDiscogs, existing.DiscogsToken));
        TheAudioDbKeySaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedAudioDb, existing.TheAudioDbApiKey));
        return Page();
    }

    private void LoadForm()
    {
        var lf = lastFm.CurrentValue;
        var d = discogs.CurrentValue;
        var a = audioDb.CurrentValue;
        var mb = musicBrainz.CurrentValue;
        var agg = aggregation.CurrentValue;
        LastFmKeySaved = !string.IsNullOrWhiteSpace(lf.ApiKey);
        DiscogsTokenSaved = !string.IsNullOrWhiteSpace(d.Token);
        TheAudioDbKeySaved = !string.IsNullOrWhiteSpace(a.ApiKey);
        Input = new UserSettingsModel
        {
            LastFmUsername = lf.Username,
            MusicBrainzContact = mb.Contact,
            MusicBrainzBaseUrl = string.Equals(mb.ResolvedBaseUrl, MusicBrainzOptions.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase)
                ? ""
                : mb.ResolvedBaseUrl,
            MusicBrainzCoverArtBaseUrl = string.Equals(mb.ResolvedCoverArtBaseUrl, MusicBrainzOptions.DefaultCoverArtBaseUrl, StringComparison.OrdinalIgnoreCase)
                ? ""
                : mb.ResolvedCoverArtBaseUrl,
            MusicBrainzMinIntervalMs = mb.MinIntervalMs,
            EnableMusicBrainz = agg.EnableMusicBrainz,
            EnableLastFmTrackInfo = agg.EnableLastFmTrackInfo,
            EnableDiscogs = agg.EnableDiscogs,
            EnableTheAudioDb = agg.EnableTheAudioDb,
            EnableVocaDb = agg.EnableVocaDb,
            EnableUtaiteDb = agg.EnableUtaiteDb,
            EnableTouhouDb = agg.EnableTouhouDb,
            IncrementalIntervalMinutes = agg.IncrementalIntervalMinutes,
            SeedSampleData = agg.SeedSampleData
        };
    }
}
