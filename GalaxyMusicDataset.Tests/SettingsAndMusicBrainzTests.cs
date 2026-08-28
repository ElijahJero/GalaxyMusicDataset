using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Http;

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
}
