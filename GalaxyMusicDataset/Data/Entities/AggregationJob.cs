using GalaxyMusicDataset.Data;

namespace GalaxyMusicDataset.Data.Entities;

public sealed class AggregationJob
{
    public long Id { get; set; }
    public JobKind Kind { get; set; }
    public JobStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
    public int ItemsSkipped { get; set; }
    public string? Message { get; set; }
    public string? DetailsJson { get; set; }
}
