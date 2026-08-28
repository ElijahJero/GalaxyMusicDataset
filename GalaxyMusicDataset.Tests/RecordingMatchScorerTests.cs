using GalaxyMusicDataset.Services.MusicBrainz;

namespace GalaxyMusicDataset.Tests;

public class RecordingMatchScorerTests
{
    [Fact]
    public void Exact_names_auto_match()
    {
        var candidate = new RecordingCandidate(
            Guid.NewGuid().ToString(),
            "Lose-Lose Days",
            "Mori Calliope",
            "UnAlive",
            210000,
            null,
            null,
            null,
            0);
        var score = RecordingMatchScorer.Score("Mori Calliope", "Lose-Lose Days", "UnAlive", candidate);
        Assert.True(score >= 0.95, score.ToString("0.000"));
        Assert.Equal(LookupDecision.AutoMatch, RecordingMatchScorer.Decide(score, 1, 0.92, 0.55));
    }

    [Fact]
    public void Weak_title_stays_not_found()
    {
        var candidate = new RecordingCandidate("x", "Completely Different Song", "Someone Else", null, null, null, null, null, 0);
        var score = RecordingMatchScorer.Score("Mori Calliope", "Lose-Lose Days", "UnAlive", candidate);
        Assert.Equal(LookupDecision.NotFound, RecordingMatchScorer.Decide(score, 0.1, 0.92, 0.55));
    }

    [Fact]
    public void Medium_score_needs_review()
    {
        Assert.Equal(LookupDecision.NeedsReview, RecordingMatchScorer.Decide(0.7, 0.9, 0.92, 0.55));
    }
}
