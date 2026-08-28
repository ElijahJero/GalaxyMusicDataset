using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Services.Aggregation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Pages;

public class SettingsModel(
    IOptionsSnapshot<LastFmOptions> lastFm,
    IOptionsSnapshot<DiscogsOptions> discogs,
    IOptionsSnapshot<TheAudioDbOptions> audioDb,
    IOptionsSnapshot<MusicBrainzOptions> musicBrainz,
    IOptionsSnapshot<AggregationOptions> aggregation,
    UserSettingsStore store) : PageModel
{
    [BindProperty]
    public UserSettingsModel Input { get; set; } = new();

    public string? Saved { get; set; }

    public void OnGet()
    {
        Input = new UserSettingsModel
        {
            LastFmApiKey = lastFm.Value.ApiKey,
            LastFmUsername = lastFm.Value.Username,
            DiscogsToken = discogs.Value.Token,
            TheAudioDbApiKey = audioDb.Value.ApiKey,
            MusicBrainzContact = musicBrainz.Value.Contact,
            EnableMusicBrainz = aggregation.Value.EnableMusicBrainz,
            EnableLastFmTrackInfo = aggregation.Value.EnableLastFmTrackInfo,
            EnableDiscogs = aggregation.Value.EnableDiscogs,
            EnableTheAudioDb = aggregation.Value.EnableTheAudioDb,
            IncrementalIntervalMinutes = aggregation.Value.IncrementalIntervalMinutes,
            SeedSampleData = aggregation.Value.SeedSampleData
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await store.SaveAsync(Input, cancellationToken);
        Saved = "Saved to App_Data/user-settings.json. Workers pick up option changes on the next item; restart if a key was just added.";
        return Page();
    }
}
