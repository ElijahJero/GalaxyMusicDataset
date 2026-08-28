namespace GalaxyMusicDataset.Configuration;

public sealed class LastFmOptions
{
    public const string SectionName = "LastFm";

    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string UserAgent { get; set; } = "GalaxyMusicDataset/0.1 (https://github.com/elijahjero/galaxymusicdataset)";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Username);
}

public sealed class MusicBrainzOptions
{
    public const string SectionName = "MusicBrainz";

    public string UserAgent { get; set; } = "GalaxyMusicDataset/0.1 (https://github.com/elijahjero/galaxymusicdataset)";
    public string Contact { get; set; } = "https://github.com/elijahjero/galaxymusicdataset";
}

public sealed class DiscogsOptions
{
    public const string SectionName = "Discogs";

    public string? Token { get; set; }
    public string UserAgent { get; set; } = "GalaxyMusicDataset/0.1 +https://github.com/elijahjero/galaxymusicdataset";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);
}

public sealed class TheAudioDbOptions
{
    public const string SectionName = "TheAudioDb";

    public string? ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    public int IncrementalIntervalMinutes { get; set; } = 60;
    public int OverlapSeconds { get; set; } = 300;
    public int LastFmPageSize { get; set; } = 200;
    public bool SeedSampleData { get; set; }
    public bool EnableMusicBrainz { get; set; } = true;
    public bool EnableLastFmTrackInfo { get; set; } = true;
    public bool EnableDiscogs { get; set; } = true;
    public bool EnableTheAudioDb { get; set; } = true;
    public double AutoMatchThreshold { get; set; } = 0.92;
    public double ReviewThreshold { get; set; } = 0.55;
    public int ApiLogRetention { get; set; } = 500;
}
