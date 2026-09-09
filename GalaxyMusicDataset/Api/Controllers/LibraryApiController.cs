using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Pages;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;
using GalaxyMusicDataset.Services.Api;
using GalaxyMusicDataset.Services.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiPolicies.Read)]
[Route("api/v1/library")]
[Produces("application/json")]
public sealed class LibraryApiController(
    AppDbContext db,
    TrackEditService editor,
    MetadataEnrichmentService enrichment,
    LibrarySearchService search) : ControllerBase
{
    public const int DefaultPageSize = 50;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? q = null,
        [FromQuery] string? artist = null,
        [FromQuery] string? title = null,
        [FromQuery] string? album = null,
        [FromQuery] string? status = null,
        [FromQuery] string? hasMbid = null,
        [FromQuery] string? hasTags = null,
        [FromQuery] string? hasAudio = null,
        [FromQuery] string? source = null,
        [FromQuery] string sort = "recent",
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        pageSize = pageSize is < 1 or > 200 ? DefaultPageSize : pageSize;

        var query = db.Tracks.AsNoTracking().AsQueryable();
        if (LibraryFilters.HasTextFilter(q, artist, title, album))
        {
            await search.EnsureCurrentAsync(db, cancellationToken);
        }

        query = LibraryFilters.Apply(query, db, search, q, artist, title, album, hasMbid, hasTags, source, status, hasAudio);
        query = sort switch
        {
            "title" => query.OrderBy(t => t.Title).ThenBy(t => t.Artist.Name),
            "artist" => query.OrderBy(t => t.Artist.Name).ThenBy(t => t.Title),
            "plays" => query.OrderByDescending(t => t.Scrobbles.Count()).ThenBy(t => t.Title),
            _ => query.OrderByDescending(t => t.Scrobbles.Max(s => (long?)s.UnixTimestamp)).ThenBy(t => t.Title)
        };

        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        if (page > totalPages)
        {
            page = totalPages;
        }

        var tracks = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(t => t.Artist).ThenInclude(a => a.Aliases)
            .Include(t => t.Album)
            .Include(t => t.Tags).ThenInclude(t => t.Tag)
            .Include(t => t.SourcePayloads)
            .Include(t => t.AudioProfile)
                .ThenInclude(p => p!.Labels)
            .ToListAsync(cancellationToken);

        var ids = tracks.Select(t => t.Id).ToList();
        var stats = await db.Scrobbles.AsNoTracking()
            .Where(s => ids.Contains(s.TrackId))
            .GroupBy(s => s.TrackId)
            .Select(g => new { TrackId = g.Key, Count = g.Count(), Last = g.Max(x => x.UnixTimestamp) })
            .ToListAsync(cancellationToken);
        var statMap = stats.ToDictionary(x => x.TrackId);
        var fingerprints = tracks.Select(t => t.Fingerprint).Distinct().ToList();
        var lookups = await db.TrackLookups.AsNoTracking()
            .Where(l => fingerprints.Contains(l.Fingerprint))
            .ToListAsync(cancellationToken);
        var lookupMap = lookups.ToDictionary(l => l.Fingerprint);

        var items = tracks.Select(t =>
        {
            statMap.TryGetValue(t.Id, out var st);
            lookupMap.TryGetValue(t.Fingerprint, out var lookup);
            return Map(t, lookup, st?.Count ?? 0, st?.Last);
        }).ToList();

        return Ok(new PagedResult<LibraryTrackDto>(items, total, page, pageSize, totalPages, page < totalPages));
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Edit(long id, [FromBody] TrackEditInput input, CancellationToken cancellationToken)
    {
        input.Id = id;
        try
        {
            var result = await editor.SaveAsync(input, cancellationToken);
            var message = result.Message;
            if (input.LookupFromMbid && !string.IsNullOrWhiteSpace(input.TrackMbid))
            {
                message += " " + await enrichment.EnrichTrackFromMbidAsync(result.TrackId, cancellationToken);
            }

            var detail = await db.Tracks.AsNoTracking()
                .Where(t => t.Id == result.TrackId)
                .Select(t => t.Id)
                .FirstAsync(cancellationToken);
            return Ok(new { trackId = detail, message, result.Merged, result.LookupQueued });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return StatusCode(status, new ApiError(ex.Message));
        }
    }

    private static LibraryTrackDto Map(Track track, TrackLookup? lookup, int playCount, long? lastUnix) =>
        new(
            track.Id,
            track.Title,
            track.ArtistId,
            track.Artist.Name,
            track.Artist.Aliases.Select(a => a.Name).Distinct().OrderBy(n => n).ToList(),
            track.AlbumId,
            track.Album?.Title,
            track.Album?.CoverUrl,
            track.Mbid,
            track.VocaDbSongId,
            track.UtaiteDbSongId,
            track.TouhouDbSongId,
            track.DurationMs,
            track.Fingerprint,
            playCount,
            lastUnix is null ? null : DateTimeOffset.FromUnixTimeSeconds(lastUnix.Value),
            lookup?.Status.ToString(),
            lookup?.BestScore,
            lookup?.ErrorMessage,
            track.Tags.Select(t => t.Tag.Name).Distinct().OrderBy(n => n).ToList(),
            track.SourcePayloads.Select(p => new SourcePayloadInfo(
                p.Source.ToString(),
                p.Status.ToString(),
                p.ExternalId,
                p.ErrorMessage,
                p.PayloadJson)).ToList(),
            track.AudioProfile is null ? null : AudioProfileService.ToView(track.AudioProfile));
}
