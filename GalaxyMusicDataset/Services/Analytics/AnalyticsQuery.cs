using GalaxyMusicDataset.Data.Entities;

namespace GalaxyMusicDataset.Services.Analytics;

public static class AnalyticsQuery
{
    public static IQueryable<Scrobble> ApplyRange(IQueryable<Scrobble> scrobbles, TimeRange range)
    {
        var fromUnix = range.From.ToUnixTimeSeconds();
        var toUnix = range.To.ToUnixTimeSeconds();
        return scrobbles.Where(s => s.UnixTimestamp >= fromUnix && s.UnixTimestamp < toUnix);
    }

    public static IQueryable<Scrobble> ApplySearch(IQueryable<Scrobble> scrobbles, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return scrobbles;
        }

        var term = search.Trim().ToLower();
        return scrobbles.Where(s =>
            s.Track.Title.ToLower().Contains(term) ||
            s.Track.Artist.Name.ToLower().Contains(term) ||
            (s.Track.Album != null && s.Track.Album.Title.ToLower().Contains(term)) ||
            s.Track.Artist.Aliases.Any(a => a.Name.ToLower().Contains(term)));
    }

    public static IQueryable<Scrobble> Apply(IQueryable<Scrobble> scrobbles, TimeRange range, string? search) =>
        ApplySearch(ApplyRange(scrobbles, range), search);
}
