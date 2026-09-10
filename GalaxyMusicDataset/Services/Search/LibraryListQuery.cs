using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Search;

public sealed record LibraryListItem(
    Track Track,
    TrackLookup? Lookup,
    int PlayCount,
    long? LastPlayedUnix,
    AudioProfileView? Audio,
    IReadOnlyList<SourcePayloadInfo> Sources);

public static class LibraryListQuery
{
    public static IQueryable<Track> ApplySort(IQueryable<Track> query, string? sort) =>
        sort switch
        {
            "title" => query.OrderBy(t => t.Title).ThenBy(t => t.Artist.Name),
            "artist" => query.OrderBy(t => t.Artist.Name).ThenBy(t => t.Title),
            "plays" => query.OrderByDescending(t => t.Scrobbles.Count()).ThenBy(t => t.Title),
            _ => query.OrderByDescending(t => t.Scrobbles.Max(s => (long?)s.UnixTimestamp)).ThenBy(t => t.Title)
        };

    public static async Task<(IReadOnlyList<LibraryListItem> Items, int TotalCount, int Page, int TotalPages)> LoadPageAsync(
        AppDbContext db,
        IQueryable<Track> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 1;
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages)
        {
            page = totalPages;
        }

        var tracks = await query
            .AsSplitQuery()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(t => t.Artist).ThenInclude(a => a.Aliases)
            .Include(t => t.Album)
            .Include(t => t.Tags).ThenInclude(t => t.Tag)
            .ToListAsync(cancellationToken);

        var ids = tracks.Select(t => t.Id).ToList();
        var fingerprints = tracks.Select(t => t.Fingerprint).Distinct().ToList();

        var stats = ids.Count == 0
            ? []
            : await db.Scrobbles.AsNoTracking()
                .Where(s => ids.Contains(s.TrackId))
                .GroupBy(s => s.TrackId)
                .Select(g => new { TrackId = g.Key, Count = g.Count(), Last = g.Max(x => x.UnixTimestamp) })
                .ToListAsync(cancellationToken);
        var statMap = stats.ToDictionary(x => x.TrackId);

        var lookups = fingerprints.Count == 0
            ? []
            : await db.TrackLookups.AsNoTracking()
                .Where(l => fingerprints.Contains(l.Fingerprint))
                .ToListAsync(cancellationToken);
        var lookupMap = lookups.ToDictionary(l => l.Fingerprint);

        var audioByTrack = await LoadAudioWithoutRawJsonAsync(db, ids, cancellationToken);
        var sourcesByTrack = await LoadSourceSummariesAsync(db, ids, cancellationToken);

        var items = tracks.Select(t =>
        {
            statMap.TryGetValue(t.Id, out var st);
            audioByTrack.TryGetValue(t.Id, out var audio);
            sourcesByTrack.TryGetValue(t.Id, out var sources);
            return new LibraryListItem(
                t,
                lookupMap.GetValueOrDefault(t.Fingerprint),
                st?.Count ?? 0,
                st?.Last,
                audio,
                sources ?? []);
        }).ToList();

        return (items, totalCount, page, totalPages);
    }

    private static async Task<Dictionary<long, AudioProfileView>> LoadAudioWithoutRawJsonAsync(
        AppDbContext db,
        List<long> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var profiles = await db.TrackAudioProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.TrackId))
            .Select(p => new TrackAudioProfile
            {
                TrackId = p.TrackId,
                AnalyzedAt = p.AnalyzedAt,
                Bpm = p.Bpm,
                Key = p.Key,
                Scale = p.Scale,
                KeyStrength = p.KeyStrength,
                Loudness = p.Loudness,
                Danceability = p.Danceability,
                Acoustic = p.Acoustic,
                Electronic = p.Electronic,
                Voice = p.Voice,
                Instrumental = p.Instrumental,
                Tonal = p.Tonal,
                Timbre = p.Timbre,
                TimbreBright = p.TimbreBright,
                Approachability = p.Approachability,
                Engagement = p.Engagement,
                MoodHappy = p.MoodHappy,
                MoodSad = p.MoodSad,
                MoodAggressive = p.MoodAggressive,
                MoodRelaxed = p.MoodRelaxed,
                MoodParty = p.MoodParty
            })
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return [];
        }

        var labels = await db.TrackAudioLabels.AsNoTracking()
            .Where(l => ids.Contains(l.TrackId))
            .ToListAsync(cancellationToken);
        var labelsByTrack = labels.ToLookup(l => l.TrackId);

        var map = new Dictionary<long, AudioProfileView>(profiles.Count);
        foreach (var profile in profiles)
        {
            profile.Labels = labelsByTrack[profile.TrackId].ToList();
            map[profile.TrackId] = AudioProfileService.ToView(profile);
        }

        return map;
    }

    private static async Task<Dictionary<long, IReadOnlyList<SourcePayloadInfo>>> LoadSourceSummariesAsync(
        AppDbContext db,
        List<long> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.TrackSourcePayloads.AsNoTracking()
            .Where(p => ids.Contains(p.TrackId))
            .Select(p => new
            {
                p.TrackId,
                p.Source,
                p.Status,
                p.ExternalId,
                p.ErrorMessage
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(p => p.TrackId)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<SourcePayloadInfo> (g) => g
                    .Select(p => new SourcePayloadInfo(
                        p.Source.ToString(),
                        p.Status.ToString(),
                        p.ExternalId,
                        p.ErrorMessage,
                        null))
                    .ToList());
    }
}
