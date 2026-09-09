using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Api;
using GalaxyMusicDataset.Services.MusicBrainz;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiPolicies.Read)]
[Route("api/v1")]
[Produces("application/json")]
public sealed class LookupsApiController(AppDbContext db, MusicBrainzLookupService lookups) : ControllerBase
{
    [HttpGet("lookups")]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
    {
        take = take is < 1 or > 500 ? 200 : take;
        var groups = await db.TrackLookups
            .GroupBy(l => l.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var counts = groups.ToDictionary(x => x.Key.ToString(), x => x.Count);

        var query = db.TrackLookups.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LookupStatus>(status, out var parsed))
        {
            query = query.Where(l => l.Status == parsed);
        }

        var rows = await query.OrderByDescending(l => l.Id).Take(take).ToListAsync(cancellationToken);
        return Ok(new
        {
            counts,
            take,
            items = rows.Select(ApiMapping.Lookup).ToList()
        });
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("lookups/{id:long}/retry")]
    public async Task<IActionResult> RetryLookup(long id, CancellationToken cancellationToken)
    {
        try
        {
            await lookups.RetryAsync(id, cancellationToken);
            return Ok(new { id, retried = true });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiError(ex.Message));
        }
    }

    [HttpGet("review")]
    public async Task<IActionResult> Review([FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        take = take is < 1 or > 200 ? 50 : take;
        var pending = await db.TrackLookups
            .AsNoTracking()
            .Where(l => l.Status == LookupStatus.NeedsReview)
            .OrderByDescending(l => l.BestScore)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = pending.Select(l => new ReviewItemDto(
            ApiMapping.Lookup(l),
            MusicBrainzLookupService.ParseCandidates(l.CandidateJson))).ToList();
        return Ok(new { take, items });
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("review/{id:long}/accept")]
    public async Task<IActionResult> Accept(long id, [FromBody] AcceptReviewRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Mbid))
        {
            return BadRequest(new ApiError("mbid is required."));
        }

        try
        {
            await lookups.AcceptCandidateAsync(id, body.Mbid.Trim(), cancellationToken);
            return Ok(new { id, accepted = true, mbid = body.Mbid.Trim() });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiError(ex.Message));
        }
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("review/{id:long}/not-found")]
    public async Task<IActionResult> MarkNotFound(long id, CancellationToken cancellationToken)
    {
        try
        {
            await lookups.MarkNotFoundAsync(id, cancellationToken);
            return Ok(new { id, status = nameof(LookupStatus.NotFound) });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiError(ex.Message));
        }
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("review/{id:long}/retry")]
    public async Task<IActionResult> RetryReview(long id, CancellationToken cancellationToken)
    {
        try
        {
            await lookups.RetryAsync(id, cancellationToken);
            return Ok(new { id, retried = true });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiError(ex.Message));
        }
    }
}
