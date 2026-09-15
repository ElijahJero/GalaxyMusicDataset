using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Api;
using GalaxyMusicDataset.Services.Auth;
using GalaxyMusicDataset.Services.Discogs;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.MusicBrainz;
using GalaxyMusicDataset.Services.Search;
using GalaxyMusicDataset.Services.TheAudioDb;
using GalaxyMusicDataset.Services.Vgmdb;
using GalaxyMusicDataset.Services.VocaDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGalaxyAggregation(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.Configure<LastFmOptions>(configuration.GetSection(LastFmOptions.SectionName));
        services.Configure<MusicBrainzOptions>(configuration.GetSection(MusicBrainzOptions.SectionName));
        services.Configure<DiscogsOptions>(configuration.GetSection(DiscogsOptions.SectionName));
        services.Configure<TheAudioDbOptions>(configuration.GetSection(TheAudioDbOptions.SectionName));
        services.Configure<VocaDbOptions>(configuration.GetSection(VocaDbOptions.SectionName));
        services.Configure<UtaiteDbOptions>(configuration.GetSection(UtaiteDbOptions.SectionName));
        services.Configure<TouhouDbOptions>(configuration.GetSection(TouhouDbOptions.SectionName));
        services.Configure<VgmdbOptions>(configuration.GetSection(VgmdbOptions.SectionName));
        services.Configure<AggregationOptions>(configuration.GetSection(AggregationOptions.SectionName));
        services.Configure<AnalyticsOptions>(configuration.GetSection(AnalyticsOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var zone = AppTimeZone.FromOptions(sp.GetRequiredService<IOptions<AnalyticsOptions>>().Value);
            AnalyticsDisplay.TimeZone = zone;
            return zone;
        });
        services.AddSingleton<AdminSignIn>();

        var dbPath = Path.Combine(environment.ContentRootPath, "App_Data", "galaxy.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<ApiCallRecorder>();
        services.AddSingleton<EnrichmentSourceHealth>();
        services.AddSingleton<AggregationProgress>();
        services.AddSingleton<AggregationCoordinator>();
        services.AddHttpClient(nameof(LastFmClient));
        services.AddHttpClient(nameof(MusicBrainzClient));
        services.AddHttpClient(nameof(DiscogsClient));
        services.AddHttpClient(nameof(TheAudioDbClient));
        services.AddHttpClient(nameof(VocaDbClient))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(25));
        services.AddHttpClient(nameof(VgmdbClient))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(25));
        services.AddSingleton<ExternalClientFactory>();
        services.AddScoped<CatalogService>();
        services.AddScoped<ScrobbleIngestService>();
        services.AddScoped<ScrobbleSyncService>();
        services.AddScoped<MusicBrainzLookupService>();
        services.AddScoped<TagService>();
        services.AddScoped<MetadataEnrichmentService>();
        services.AddScoped<TrackEditService>();
        services.AddScoped<Audio.AudioProfileService>();
        services.AddScoped<SampleDataSeeder>();
        services.AddScoped<AggregationStatusService>();
        services.AddScoped<AnalyticsQueries>();
        services.AddSingleton<UserSettingsStore>();
        services.AddSingleton<ApiKeyStore>();
        services.AddSingleton<LibrarySearchService>();
        services.AddHostedService<AggregationHostedService>();
        return services;
    }
}
