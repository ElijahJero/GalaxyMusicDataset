using GalaxyMusicDataset.Services.Normalization;

namespace GalaxyMusicDataset.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void Normalize_folds_case_and_whitespace()
    {
        Assert.Equal("mori calliope", TextNormalizer.Normalize("  Mori   Calliope "));
    }

    [Fact]
    public void Normalize_strips_instrumental_marker()
    {
        var normalized = TextNormalizer.Normalize("ススメRunner!!(instrumental)");
        Assert.DoesNotContain("instrumental", normalized);
        Assert.Contains("runner", normalized);
    }

    [Fact]
    public void Fingerprint_is_stable_across_case()
    {
        var a = TrackFingerprint.Compute("Mori Calliope", "Lose-Lose Days");
        var b = TrackFingerprint.Compute("mori calliope", "lose-lose days");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void Different_titles_get_different_fingerprints()
    {
        var a = TrackFingerprint.Compute("nihmune", "Brain Rot");
        var b = TrackFingerprint.Compute("nihmune", "Shopping Malls");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ContainsCjk_detects_kanji()
    {
        Assert.True(TextNormalizer.ContainsCjk("明石繆"));
        Assert.False(TextNormalizer.ContainsCjk("Mori Calliope"));
    }

    [Fact]
    public void Kana_romanizes_for_search()
    {
        var romaji = TextNormalizer.RomanizeIfKana("すずめ");
        Assert.Equal("suzume", romaji);
    }
}
