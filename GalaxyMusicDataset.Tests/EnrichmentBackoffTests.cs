using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.VocaDb;

namespace GalaxyMusicDataset.Tests;

public class EnrichmentBackoffTests
{
    [Fact]
    public void Transient_http_500_uses_long_cooldown()
    {
        var message = EnrichmentRetryHelpers.BusyMessage("VocaDB", 500);
        Assert.True(EnrichmentRetryHelpers.IsTransientFailureMessage(message));
        Assert.Equal(TimeSpan.FromMinutes(30), EnrichmentRetryHelpers.ErrorRetryCooldown(message));
    }

    [Fact]
    public void Ordinary_errors_keep_short_cooldown()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), EnrichmentRetryHelpers.ErrorRetryCooldown("No match found."));
    }

    [Fact]
    public void JsonApiException_500_is_transient()
    {
        var ex = new JsonApiException("VocaDB", "HTTP 500", 500);
        Assert.True(EnrichmentRetryHelpers.IsTransientFailure(ex));
    }

    [Fact]
    public void Timeout_messages_are_transient_with_long_cooldown()
    {
        var message = EnrichmentRetryHelpers.BusyMessage("VocaDB", null);
        Assert.Contains("timed out", message, StringComparison.OrdinalIgnoreCase);
        Assert.True(EnrichmentRetryHelpers.IsTransientFailureMessage(message));
        Assert.Equal(TimeSpan.FromMinutes(30), EnrichmentRetryHelpers.ErrorRetryCooldown(message));
        Assert.True(EnrichmentRetryHelpers.IsTransientFailure(
            new JsonApiException("VocaDB", "VocaDB request timed out after 25000ms.", null)));
    }

    [Fact]
    public void HttpClient_timeout_is_not_treated_as_shutdown_cancel()
    {
        using var unused = new CancellationTokenSource();
        var ex = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 25 seconds elapsing.");
        Assert.True(HttpResponseHelpers.IsHttpClientTimeout(ex, CancellationToken.None));
        Assert.False(HttpResponseHelpers.IsHttpClientTimeout(ex, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Source_health_opens_circuit_after_repeated_failures()
    {
        var health = new EnrichmentSourceHealth();
        Assert.False(health.IsPaused(EnrichmentSource.VocaDb));
        Assert.False(health.RecordTransientFailure(EnrichmentSource.VocaDb, null));
        Assert.False(health.RecordTransientFailure(EnrichmentSource.VocaDb, null));
        Assert.True(health.RecordTransientFailure(EnrichmentSource.VocaDb, null));
        Assert.True(health.IsPaused(EnrichmentSource.VocaDb));
    }

    [Fact]
    public void Source_health_resets_after_success()
    {
        var health = new EnrichmentSourceHealth();
        health.RecordTransientFailure(EnrichmentSource.VocaDb, null);
        health.RecordTransientFailure(EnrichmentSource.VocaDb, null);
        health.RecordTransientFailure(EnrichmentSource.VocaDb, null);
        Assert.True(health.IsPaused(EnrichmentSource.VocaDb));
        health.RecordSuccess(EnrichmentSource.VocaDb);
        Assert.False(health.IsPaused(EnrichmentSource.VocaDb));
    }

    [Fact]
    public void Sanitize_strips_nul_and_control_chars()
    {
        Assert.Equal("World is Mine", VocaDbClient.SanitizeSearchTerm("World is Mine"));
        Assert.Equal("bad title", VocaDbClient.SanitizeSearchTerm("bad\0 title"));
        Assert.Null(VocaDbClient.SanitizeSearchTerm(" \0\t "));
        Assert.Null(VocaDbClient.SanitizeSearchTerm(null));
    }
}
