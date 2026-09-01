using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Tests;

public class UserSettingsTests
{
    [Fact]
    public void KeepIfBlank_preserves_existing_secret()
    {
        Assert.Equal("saved-key", UserSettingsStore.KeepIfBlank(null, "saved-key"));
        Assert.Equal("saved-key", UserSettingsStore.KeepIfBlank("", "saved-key"));
        Assert.Equal("saved-key", UserSettingsStore.KeepIfBlank("   ", "saved-key"));
    }

    [Fact]
    public void KeepIfBlank_replaces_when_posted()
    {
        Assert.Equal("new-key", UserSettingsStore.KeepIfBlank("new-key", "saved-key"));
        Assert.Equal("new-key", UserSettingsStore.KeepIfBlank(" new-key ", "saved-key"));
    }

    [Fact]
    public void KeepIfBlank_stays_empty_when_nothing_stored()
    {
        Assert.Null(UserSettingsStore.KeepIfBlank("", null));
        Assert.Null(UserSettingsStore.KeepIfBlank(null, null));
    }
}

public class MusicBrainzBackoffTests
{
    [Fact]
    public void BusyBackoff_is_at_least_five_seconds_on_first_503()
    {
        var delay = HttpResponseHelpers.BusyBackoff(null, 1);
        Assert.True(delay >= TimeSpan.FromSeconds(5), delay.ToString());
    }

    [Fact]
    public void BusyBackoff_grows_with_attempts()
    {
        var first = HttpResponseHelpers.BusyBackoff(null, 1);
        var third = HttpResponseHelpers.BusyBackoff(null, 3);
        Assert.True(third > first);
        Assert.True(third <= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Transient_statuses_match_musicbrainz_busy()
    {
        Assert.True(HttpResponseHelpers.IsTransientStatus(503));
        Assert.True(HttpResponseHelpers.IsTransientStatus(429));
        Assert.False(HttpResponseHelpers.IsTransientStatus(200));
        Assert.False(HttpResponseHelpers.IsTransientStatus(404));
    }

    [Fact]
    public void Busy_lookups_are_not_due_immediately()
    {
        var lookup = new TrackLookup
        {
            Status = LookupStatus.Pending,
            LastAttemptUtc = DateTimeOffset.UtcNow,
            ErrorMessage = "MusicBrainz busy (HTTP 503); will retry after cooldown."
        };
        Assert.False(MusicBrainzLookupService.IsLookupDue(lookup, DateTimeOffset.UtcNow));
        Assert.True(MusicBrainzLookupService.IsLookupDue(lookup, DateTimeOffset.UtcNow.AddSeconds(31)));
    }

    [Fact]
    public void Fresh_pending_lookup_is_due()
    {
        var lookup = new TrackLookup { Status = LookupStatus.Pending };
        Assert.True(MusicBrainzLookupService.IsLookupDue(lookup, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Http_busy_does_not_mean_not_found()
    {
        var ex = new JsonApiException("MusicBrainz", "busy", 503);
        Assert.True(HttpResponseHelpers.IsTransientStatus(ex.StatusCode));
    }

    [Fact]
    public void Old_gave_up_errors_are_treated_as_transient()
    {
        Assert.True(MusicBrainzLookupService.IsTransientFailureMessage(
            "Gave up after 5 failed MusicBrainz attempts."));
        Assert.True(MusicBrainzLookupService.IsTransientFailureMessage(
            "MusicBrainz busy (HTTP 503); will retry after cooldown."));
        Assert.False(MusicBrainzLookupService.IsTransientFailureMessage(
            "No MusicBrainz recordings returned."));
        Assert.False(MusicBrainzLookupService.IsTransientFailureMessage(
            "Best score 0.40 below review threshold."));
    }

    [Fact]
    public async Task Postpone_delays_the_next_wait()
    {
        var limiter = new ApiRateLimiter(TimeSpan.FromMilliseconds(5));
        await limiter.WaitAsync(CancellationToken.None);
        limiter.Postpone(TimeSpan.FromMilliseconds(180));
        var started = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync(CancellationToken.None);
        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(120), started.Elapsed.ToString());
    }

    [Fact]
    public async Task Requeue_only_transient_not_found()
    {
        await using var harness = await TestDb.CreateAsync();
        harness.Db.TrackLookups.AddRange(
            new TrackLookup
            {
                Fingerprint = "gave-up",
                ArtistName = "A",
                TrackName = "Busy song",
                Status = LookupStatus.NotFound,
                ErrorMessage = "Gave up after 5 failed MusicBrainz attempts.",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new TrackLookup
            {
                Fingerprint = "missing",
                ArtistName = "B",
                TrackName = "Unknown song",
                Status = LookupStatus.NotFound,
                ErrorMessage = "No MusicBrainz recordings returned.",
                CreatedAt = DateTimeOffset.UtcNow
            });
        await harness.Db.SaveChangesAsync();

        var service = new MusicBrainzLookupService(
            harness.Db,
            new CatalogService(harness.Db),
            null!,
            new AggregationProgress(),
            new StaticMonitor<AggregationOptions>(new AggregationOptions()));

        var requeued = await service.RequeueTransientFailuresAsync(CancellationToken.None);
        Assert.Equal(1, requeued);
        Assert.Equal(
            LookupStatus.Pending,
            await harness.Db.TrackLookups.Where(l => l.Fingerprint == "gave-up").Select(l => l.Status).SingleAsync());
        Assert.Equal(
            LookupStatus.NotFound,
            await harness.Db.TrackLookups.Where(l => l.Fingerprint == "missing").Select(l => l.Status).SingleAsync());
    }

    [Fact]
    public async Task PickNext_skips_recent_cooldown_rows_so_a_later_due_lookup_runs()
    {
        await using var harness = await TestDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 60; i++)
        {
            harness.Db.TrackLookups.Add(new TrackLookup
            {
                Fingerprint = $"busy-{i}",
                ArtistName = "A",
                TrackName = $"Busy {i}",
                Status = LookupStatus.Pending,
                ErrorMessage = "MusicBrainz busy (HTTP 503); will retry after cooldown.",
                LastAttemptUtc = now,
                CreatedAt = now
            });
        }

        harness.Db.TrackLookups.Add(new TrackLookup
        {
            Fingerprint = "due",
            ArtistName = "B",
            TrackName = "Ready",
            Status = LookupStatus.Pending,
            CreatedAt = now
        });
        await harness.Db.SaveChangesAsync();

        var service = new MusicBrainzLookupService(
            harness.Db,
            new CatalogService(harness.Db),
            null!,
            new AggregationProgress(),
            new StaticMonitor<AggregationOptions>(new AggregationOptions()));

        var next = await service.PickNextLookupAsync(CancellationToken.None);
        Assert.NotNull(next);
        Assert.Equal("due", next!.Fingerprint);
    }
}

file sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable OnChange(Action<T, string?> listener) => new Noop();

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
