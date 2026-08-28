namespace GalaxyMusicDataset.Data.Entities;

public sealed class ApiRequestLog
{
    public long Id { get; set; }
    public string Source { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public int? StatusCode { get; set; }
    public bool Success { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset At { get; set; }
}
