using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Discogs;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.ListenBrainz;
using GalaxyMusicDataset.Services.Maloja;
using GalaxyMusicDataset.Services.MusicBrainz;
using GalaxyMusicDataset.Services.Scrobbling;
using GalaxyMusicDataset.Services.TheAudioDb;
using GalaxyMusicDataset.Services.Vgmdb;
using GalaxyMusicDataset.Services.VocaDb;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services;

public sealed class ExternalClientFactory(
    IHttpClientFactory httpClientFactory,
    ApiCallRecorder recorder,
    IOptionsMonitor<LastFmOptions> lastFmOptions,
    IOptionsMonitor<LibreFmOptions> libreFmOptions,
    IOptionsMonitor<MalojaOptions> malojaOptions,
    IOptionsMonitor<ListenBrainzOptions> listenBrainzOptions,
    IOptionsMonitor<MusicBrainzOptions> musicBrainzOptions,
    IOptionsMonitor<DiscogsOptions> discogsOptions,
    IOptionsMonitor<TheAudioDbOptions> audioDbOptions,
    IOptionsMonitor<VocaDbOptions> vocaDbOptions,
    IOptionsMonitor<UtaiteDbOptions> utaiteDbOptions,
    IOptionsMonitor<TouhouDbOptions> touhouDbOptions,
    IOptionsMonitor<VgmdbOptions> vgmdbOptions,
    IOptionsMonitor<AggregationOptions> aggregationOptions)
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
            BaseUrl = options.ResolvedBaseUrl,
            UserAgent = options.UserAgent,
            Provider = ScrobbleProvider.LastFm,
            DisplayName = "Last.fm"
        };
    }

    public LastFmClient? TryCreateLibreFm()
    {
        var options = libreFmOptions.CurrentValue;
        if (!options.IsConfigured)
        {
            return null;
        }

        return new LastFmClient(httpClientFactory.CreateClient(nameof(LastFmClient)), recorder)
        {
            ApiKey = options.ApiKey!,
            Username = options.Username!,
            BaseUrl = options.ResolvedBaseUrl,
            UserAgent = options.UserAgent,
            Provider = ScrobbleProvider.LibreFm,
            DisplayName = "Libre.fm"
        };
    }

    public MalojaClient? TryCreateMaloja()
    {
        var options = malojaOptions.CurrentValue;
        if (!options.IsConfigured)
        {
            return null;
        }

        MalojaClient.RateLimiter.SetMinInterval(options.MinInterval);
        return new MalojaClient(httpClientFactory.CreateClient(nameof(MalojaClient)), recorder)
        {
            BaseUrl = options.ResolvedBaseUrl,
            ApiKey = options.ApiKey,
            UserAgent = options.UserAgent
        };
    }

    public ListenBrainzClient? TryCreateListenBrainz()
    {
        var options = listenBrainzOptions.CurrentValue;
        if (!options.IsConfigured)
        {
            return null;
        }

        ListenBrainzClient.RateLimiter.SetMinInterval(options.MinInterval);
        return new ListenBrainzClient(httpClientFactory.CreateClient(nameof(ListenBrainzClient)), recorder)
        {
            Username = options.Username!,
            Token = options.Token,
            BaseUrl = options.ResolvedBaseUrl,
            UserAgent = options.UserAgent
        };
    }

    public IScrobbleHistoryClient? TryCreateScrobbleClient(ScrobbleProvider? provider = null)
    {
        var target = provider ?? aggregationOptions.CurrentValue.ResolvedPrimaryScrobbleSource;
        return target switch
        {
            ScrobbleProvider.LastFm => TryCreateLastFm(),
            ScrobbleProvider.LibreFm => TryCreateLibreFm(),
            ScrobbleProvider.Maloja => TryCreateMaloja(),
            ScrobbleProvider.ListenBrainz => TryCreateListenBrainz(),
            _ => null
        };
    }

    public bool IsScrobbleProviderConfigured(ScrobbleProvider provider) => provider switch
    {
        ScrobbleProvider.LastFm => lastFmOptions.CurrentValue.IsConfigured,
        ScrobbleProvider.LibreFm => libreFmOptions.CurrentValue.IsConfigured,
        ScrobbleProvider.Maloja => malojaOptions.CurrentValue.IsConfigured,
        ScrobbleProvider.ListenBrainz => listenBrainzOptions.CurrentValue.IsConfigured,
        _ => false
    };

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

    public VgmdbClient CreateVgmdb()
    {
        var options = vgmdbOptions.CurrentValue;
        VgmdbClient.RateLimiter.SetMinInterval(options.MinInterval);
        return new VgmdbClient(httpClientFactory.CreateClient(nameof(VgmdbClient)), recorder)
        {
            BaseUrl = options.ResolvedBaseUrl,
            UserAgent = options.UserAgent
        };
    }
}
