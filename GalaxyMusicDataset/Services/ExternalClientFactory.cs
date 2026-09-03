using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Discogs;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.MusicBrainz;
using GalaxyMusicDataset.Services.TheAudioDb;
using GalaxyMusicDataset.Services.VocaDb;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services;

public sealed class ExternalClientFactory(
    IHttpClientFactory httpClientFactory,
    ApiCallRecorder recorder,
    IOptionsMonitor<LastFmOptions> lastFmOptions,
    IOptionsMonitor<MusicBrainzOptions> musicBrainzOptions,
    IOptionsMonitor<DiscogsOptions> discogsOptions,
    IOptionsMonitor<TheAudioDbOptions> audioDbOptions,
    IOptionsMonitor<VocaDbOptions> vocaDbOptions,
    IOptionsMonitor<UtaiteDbOptions> utaiteDbOptions,
    IOptionsMonitor<TouhouDbOptions> touhouDbOptions)
{
    public LastFmClient? TryCreateLastFm()
    {
        var options = lastFmOptions.CurrentValue;
        if (!options.IsConfigured)
        {
            return null;
        }

        return new LastFmClient(httpClientFactory.CreateClient(nameof(LastFmClient)), recorder)
        {
            ApiKey = options.ApiKey!,
            Username = options.Username!,
            UserAgent = options.UserAgent
        };
    }

    public MusicBrainzClient CreateMusicBrainz()
    {
        var options = musicBrainzOptions.CurrentValue;
        MusicBrainzClient.RateLimiter.SetMinInterval(options.WebServiceMinInterval);
        MusicBrainzClient.CoverArtRateLimiter.SetMinInterval(options.CoverArtMinInterval);
        var ua = string.IsNullOrWhiteSpace(options.Contact)
            ? options.UserAgent
            : $"{options.UserAgent} ({options.Contact})";
        return new MusicBrainzClient(httpClientFactory.CreateClient(nameof(MusicBrainzClient)), recorder)
        {
            UserAgent = ua,
            Options = options
        };
    }

    public DiscogsClient? TryCreateDiscogs()
    {
        var options = discogsOptions.CurrentValue;
        if (!options.IsConfigured)
        {
            return null;
        }

        return new DiscogsClient(httpClientFactory.CreateClient(nameof(DiscogsClient)), recorder)
        {
            Token = options.Token!,
            UserAgent = options.UserAgent
        };
    }

    public TheAudioDbClient? TryCreateTheAudioDb()
    {
        var options = audioDbOptions.CurrentValue;
        if (!options.IsConfigured)
        {
            return null;
        }

        return new TheAudioDbClient(httpClientFactory.CreateClient(nameof(TheAudioDbClient)), recorder)
        {
            ApiKey = options.ApiKey!
        };
    }

    public VocaDbClient? TryCreateVocaDbFamily(EnrichmentSource source)
    {
        VocaDbSiteOptions? options = source switch
        {
            EnrichmentSource.VocaDb => vocaDbOptions.CurrentValue,
            EnrichmentSource.UtaiteDb => utaiteDbOptions.CurrentValue,
            EnrichmentSource.TouhouDb => touhouDbOptions.CurrentValue,
            _ => null
        };
        if (options is null)
        {
            return null;
        }

        var limiter = VocaDbClient.LimiterFor(source);
        limiter.SetMinInterval(options.MinInterval);
        return new VocaDbClient(httpClientFactory.CreateClient(nameof(VocaDbClient)), recorder)
        {
            BaseUrl = options.ResolvedBaseUrl,
            SourceName = VocaDbFamily.DisplayName(source),
            UserAgent = options.UserAgent,
            RateLimiter = limiter
        };
    }
}
