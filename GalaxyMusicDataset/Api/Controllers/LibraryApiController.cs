using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Pages;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Api;
using GalaxyMusicDataset.Services.Search;
using GalaxyMusicDataset.Services.YouTube;
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
        [FromQuery] string? audioKind = null,
        [FromQuery] string? audioLabel = null,
        [FromQuery] double? bpmMin = null,
        [FromQuery] double? bpmMax = null,
        [FromQuery] string? bpmBucket = null,
        [FromQuery] string? audioKey = null,
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

        query = LibraryFilters.Apply(
            query, db, search, q, artist, title, album, hasMbid, hasTags, source, status,
            hasAudio, audioKind, audioLabel, bpmMin, bpmMax, bpmBucket, audioKey);
        query = LibraryListQuery.ApplySort(query, sort);

        var loaded = await LibraryListQuery.LoadPageAsync(db, query, page, pageSize, cancellationToken);
        var items = loaded.Items.Select(item => Map(item)).ToList();

        return Ok(new PagedResult<LibraryTrackDto>(
            items,
            loaded.TotalCount,
            loaded.Page,
            pageSize,
            loaded.TotalPages,
            loaded.Page < loaded.TotalPages));
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

    private static LibraryTrackDto Map(LibraryListItem item)
    {
        var track = item.Track;
        return new(
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
            item.PlayCount,
            item.LastPlayedUnix is null ? null : DateTimeOffset.FromUnixTimeSeconds(item.LastPlayedUnix.Value),
            item.Lookup?.Status.ToString(),
            item.Lookup?.BestScore,
            item.Lookup?.ErrorMessage,
            track.Tags.Select(t => t.Tag.Name).Distinct().OrderBy(n => n).ToList(),
            item.Sources,
            item.Audio,
            YouTubeMusicLinks.OpenUrl(track.Artist.Name, track.Title, track.MusicVideoUrl));
    }
}
