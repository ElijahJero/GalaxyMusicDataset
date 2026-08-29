using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Discogs;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.MusicBrainz;
using GalaxyMusicDataset.Services.TheAudioDb;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGalaxyAggregation(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.Configure<LastFmOptions>(configuration.GetSection(LastFmOptions.SectionName));
        services.Configure<MusicBrainzOptions>(configuration.GetSection(MusicBrainzOptions.SectionName));
        services.Configure<DiscogsOptions>(configuration.GetSection(DiscogsOptions.SectionName));
        services.Configure<TheAudioDbOptions>(configuration.GetSection(TheAudioDbOptions.SectionName));
        services.Configure<AggregationOptions>(configuration.GetSection(AggregationOptions.SectionName));

        var dbPath = Path.Combine(environment.ContentRootPath, "App_Data", "galaxy.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<ApiCallRecorder>();
        services.AddSingleton<AggregationProgress>();
        services.AddSingleton<AggregationCoordinator>();
        services.AddHttpClient(nameof(LastFmClient));
        services.AddHttpClient(nameof(MusicBrainzClient));
        services.AddHttpClient(nameof(DiscogsClient));
        services.AddHttpClient(nameof(TheAudioDbClient));
        services.AddSingleton<ExternalClientFactory>();
        services.AddScoped<CatalogService>();
        services.AddScoped<ScrobbleIngestService>();
        services.AddScoped<ScrobbleSyncService>();
        services.AddScoped<MusicBrainzLookupService>();
        services.AddScoped<TagService>();
        services.AddScoped<MetadataEnrichmentService>();
        services.AddScoped<TrackEditService>();
        services.AddScoped<SampleDataSeeder>();
        services.AddScoped<AggregationStatusService>();
        services.AddScoped<AnalyticsQueries>();
        services.AddSingleton<UserSettingsStore>();
        services.AddHostedService<AggregationHostedService>();
        return services;
    }
}
