using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.LastFm;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Tests;

public class ScrobbleReconcileTests
{
    [Fact]
    public void Nightly_check_waits_a_day_and_respects_pause()
    {
        var now = new DateTimeOffset(2024, 6, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.True(ScrobbleReconcile.NightlyCheckDue(null, now, paused: false));
        Assert.False(ScrobbleReconcile.NightlyCheckDue(null, now, paused: true));
        Assert.False(ScrobbleReconcile.NightlyCheckDue(now.AddHours(-23), now, paused: false));
        Assert.True(ScrobbleReconcile.NightlyCheckDue(now.AddHours(-24), now, paused: false));
        Assert.False(ScrobbleReconcile.NightlyCheckDue(now.AddDays(-2), now, paused: true));
    }

    [Fact]
    public void Utc_day_window_matches_the_backfill_bounds()
    {
        var (from, to) = ScrobbleReconcile.UtcDayRange(new DateTime(2024, 6, 1));
        Assert.Equal(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), from);
        Assert.Equal(new DateTimeOffset(2024, 6, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), to);

        var (apiFrom, apiTo) = ScrobbleReconcile.LastFmWindow(from, to);
        Assert.Equal(from - 1, apiFrom);
        Assert.Equal(to, apiTo);
    }

    [Fact]
    public async Task Complete_window_inserts_missing_plays_and_removes_deleted_ones()
    {
        await using var harness = await TestDb.CreateAsync();
        var service = new ScrobbleReconcileService(harness.Db, new ScrobbleIngestService(harness.Db, new CatalogService(harness.Db)));
        var (from, to) = ScrobbleReconcile.UtcDayRange(new DateTime(2024, 6, 1));
        var keep = from + 100;
        var deleted = from + 200;
        var outside = to + 50;

        await service.ApplyWindowAsync(
            from,
            to,
            Window(true, Track(keep, "Keep"), Track(deleted, "Gone"), Track(outside, "Outside")),
            CancellationToken.None);

        var result = await service.ApplyWindowAsync(
            from,
            to,
            Window(true, Track(keep, "Keep"), Track(from + 300, "New")),
            CancellationToken.None);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Removed);
        Assert.False(result.DeletesSkipped);
        Assert.Equal(
            new[] { keep, from + 300, outside },
            await harness.Db.Scrobbles.OrderBy(s => s.UnixTimestamp).Select(s => s.UnixTimestamp).ToListAsync());
    }

    [Fact]
    public async Task Incomplete_window_does_not_delete()
    {
        await using var harness = await TestDb.CreateAsync();
        var service = new ScrobbleReconcileService(harness.Db, new ScrobbleIngestService(harness.Db, new CatalogService(harness.Db)));
        var (from, to) = ScrobbleReconcile.UtcDayRange(new DateTime(2024, 6, 1));
        var keep = from + 100;
        var deleted = from + 200;
        await service.ApplyWindowAsync(from, to, Window(true, Track(keep, "Keep"), Track(deleted, "Gone")), CancellationToken.None);

        var result = await service.ApplyWindowAsync(
            from,
            to,
            new LastFmWindowResult([Track(keep, "Keep")], 5, 1, false, "page gap"),
            CancellationToken.None);

        Assert.True(result.DeletesSkipped);
        Assert.Equal(0, result.Removed);
        Assert.Equal(2, await harness.Db.Scrobbles.CountAsync());
    }

    private static LastFmWindowResult Window(bool ok, params LastFmRecentTrack[] tracks) =>
        new(tracks, tracks.Length, 1, ok, ok ? null : "incomplete");

    private static LastFmRecentTrack Track(long unix, string title) =>
        new("Artist", title, "Album", unix, null, null, null, false, "{}");
}
