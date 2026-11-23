using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Services;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Pages;

[Authorize]
public class SettingsModel(
    IOptionsMonitor<LastFmOptions> lastFm,
    IOptionsMonitor<LibreFmOptions> libreFm,
    IOptionsMonitor<MalojaOptions> maloja,
    IOptionsMonitor<ListenBrainzOptions> listenBrainz,
    IOptionsMonitor<DiscogsOptions> discogs,
    IOptionsMonitor<TheAudioDbOptions> audioDb,
    IOptionsMonitor<MusicBrainzOptions> musicBrainz,
    IOptionsMonitor<VgmdbOptions> vgmdb,
    IOptionsMonitor<AggregationOptions> aggregation,
    IOptionsMonitor<UiOptions> ui,
    ExternalClientFactory clients,
    UserSettingsStore store,
    ApiKeyStore apiKeys,
    AggregationCoordinator coordinator) : PageModel
{
    [BindProperty]
    public UserSettingsModel Input { get; set; } = new();

    [BindProperty]
    public string NewApiKeyName { get; set; } = "";

    [BindProperty]
    public bool NewApiKeyRead { get; set; } = true;

    [BindProperty]
    public bool NewApiKeyWrite { get; set; } = true;

    public string? Saved { get; set; }
    public string? ApiKeyError { get; set; }
    public string? CreatedApiKey { get; set; }
    public string? CreatedApiKeyName { get; set; }
    public bool LastFmKeySaved { get; set; }
    public bool LibreFmKeySaved { get; set; }
    public bool MalojaKeySaved { get; set; }
    public bool ListenBrainzTokenSaved { get; set; }
    public bool DiscogsTokenSaved { get; set; }
    public bool TheAudioDbKeySaved { get; set; }
    public bool PrimaryConfigured { get; set; }
    public string PrimarySourceName { get; set; } = "Last.fm";
    public IReadOnlyList<ApiKeyInfo> ApiKeys { get; private set; } = [];

    public void OnGet() => LoadForm();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var existing = new StoredSecrets(
            lastFm.CurrentValue.ApiKey,
            discogs.CurrentValue.Token,
            audioDb.CurrentValue.ApiKey,
            libreFm.CurrentValue.ApiKey,
            maloja.CurrentValue.ApiKey,
            listenBrainz.CurrentValue.Token);
        await store.SaveAsync(Input, existing, cancellationToken);
        Saved = "Saved. Blank secret fields were left unchanged.";
        var postedKey = Input.LastFmApiKey;
        var postedLibre = Input.LibreFmApiKey;
        var postedMaloja = Input.MalojaApiKey;
        var postedLb = Input.ListenBrainzToken;
        var postedDiscogs = Input.DiscogsToken;
        var postedAudioDb = Input.TheAudioDbApiKey;
        LoadForm();
        LastFmKeySaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedKey, existing.LastFmApiKey));
        LibreFmKeySaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedLibre, existing.LibreFmApiKey));
        MalojaKeySaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedMaloja, existing.MalojaApiKey));
        ListenBrainzTokenSaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedLb, existing.ListenBrainzToken));
        DiscogsTokenSaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedDiscogs, existing.DiscogsToken));
        TheAudioDbKeySaved = !string.IsNullOrWhiteSpace(UserSettingsStore.KeepIfBlank(postedAudioDb, existing.TheAudioDbApiKey));
        return Page();
    }

    public IActionResult OnPostReconcileFull()
    {
        var primary = aggregation.CurrentValue.ResolvedPrimaryScrobbleSource;
        if (!clients.IsScrobbleProviderConfigured(primary))
        {
            Saved = $"Primary scrobble source '{primary}' is not configured.";
            LoadForm();
            return Page();
        }

        coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.ReconcileFull));
        return RedirectToPage("/Index");
    }

    public IActionResult OnPostCreateApiKey()
    {
        try
        {
            var created = apiKeys.Create(NewApiKeyName, NewApiKeyRead, NewApiKeyWrite);
            CreatedApiKey = created.Token;
            CreatedApiKeyName = created.Info.Name;
            NewApiKeyName = "";
        }
        catch (InvalidOperationException ex)
        {
            ApiKeyError = ex.Message;
        }

        LoadForm();
        return Page();
    }

    public IActionResult OnPostRevokeApiKey(string id)
    {
        if (!apiKeys.Revoke(id))
        {
            ApiKeyError = "That API key was not found or is already revoked.";
        }
        else
        {
            Saved = "API key revoked.";
        }

        LoadForm();
        return Page();
    }

    private void LoadForm()
    {
        var lf = lastFm.CurrentValue;
        var libre = libreFm.CurrentValue;
        var mj = maloja.CurrentValue;
        var lb = listenBrainz.CurrentValue;
        var d = discogs.CurrentValue;
        var a = audioDb.CurrentValue;
        var mb = musicBrainz.CurrentValue;
        var agg = aggregation.CurrentValue;
        var primary = agg.ResolvedPrimaryScrobbleSource;
        LastFmKeySaved = !string.IsNullOrWhiteSpace(lf.ApiKey);
        LibreFmKeySaved = !string.IsNullOrWhiteSpace(libre.ApiKey);
        MalojaKeySaved = !string.IsNullOrWhiteSpace(mj.ApiKey);
        ListenBrainzTokenSaved = !string.IsNullOrWhiteSpace(lb.Token);
        PrimaryConfigured = clients.IsScrobbleProviderConfigured(primary);
        PrimarySourceName = primary.ToString();
        DiscogsTokenSaved = !string.IsNullOrWhiteSpace(d.Token);
        TheAudioDbKeySaved = !string.IsNullOrWhiteSpace(a.ApiKey);
        ApiKeys = apiKeys.List();
        Input = new UserSettingsModel
        {
            PrimaryScrobbleSource = primary.ToString(),
            CrossProviderMatchSeconds = agg.CrossProviderMatchSeconds,
            LastFmUsername = lf.Username,
            LibreFmUsername = libre.Username,
            LibreFmBaseUrl = string.Equals(libre.ResolvedBaseUrl, LibreFmOptions.DefaultBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                ? ""
                : libre.ResolvedBaseUrl,
            MalojaBaseUrl = mj.IsConfigured ? mj.ResolvedBaseUrl : "",
            MalojaMinIntervalMs = mj.MinIntervalMs,
            ListenBrainzUsername = lb.Username,
            ListenBrainzBaseUrl = string.Equals(lb.ResolvedBaseUrl, ListenBrainzOptions.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase)
                ? ""
                : lb.ResolvedBaseUrl,
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
            EnableMalojaAlbumArt = agg.EnableMalojaAlbumArt,
            EnableDiscogs = agg.EnableDiscogs,
            EnableTheAudioDb = agg.EnableTheAudioDb,
            EnableVocaDb = agg.EnableVocaDb,
            EnableUtaiteDb = agg.EnableUtaiteDb,
            EnableTouhouDb = agg.EnableTouhouDb,
            EnableVgmdb = agg.EnableVgmdb,
            VgmdbBaseUrl = string.Equals(vgmdb.CurrentValue.ResolvedBaseUrl, VgmdbOptions.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase)
                ? ""
                : vgmdb.CurrentValue.ResolvedBaseUrl,
            IncrementalIntervalMinutes = agg.IncrementalIntervalMinutes,
            SeedSampleData = agg.SeedSampleData,
            DefaultTheme = UiOptions.NormalizeTheme(ui.CurrentValue.DefaultTheme)
        };
    }
}
