namespace GalaxyMusicDataset.Data.Entities;

public sealed class TrackAudioProfile
{
    public long TrackId { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    public double? Bpm { get; set; }
    public string? Key { get; set; }
    public string? Scale { get; set; }
    public double? KeyStrength { get; set; }
    public double? Loudness { get; set; }
    public double? Danceability { get; set; }
    public double? Acoustic { get; set; }
    public double? Electronic { get; set; }
    public double? Voice { get; set; }
    public double? Instrumental { get; set; }
    public double? Tonal { get; set; }
    public string? Timbre { get; set; }
    public double? TimbreBright { get; set; }
    public double? Approachability { get; set; }
    public double? Engagement { get; set; }
    public double? MoodHappy { get; set; }
    public double? MoodSad { get; set; }
    public double? MoodAggressive { get; set; }
    public double? MoodRelaxed { get; set; }
    public double? MoodParty { get; set; }
    public string? RawJson { get; set; }

    public Track Track { get; set; } = null!;
    public ICollection<TrackAudioLabel> Labels { get; set; } = new List<TrackAudioLabel>();
}
