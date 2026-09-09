using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiPolicies.Read)]
[Route("api/v1")]
[Produces("application/json")]
public sealed class AnalyticsApiController(AnalyticsQueries analytics, AppTimeZone zone) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var overview = await analytics.GetOverview(window, q, cancellationToken);
        var tags = await analytics.GetTagCloud(window, q, 8, cancellationToken);
        var years = await analytics.GetYears(cancellationToken);
        return Ok(new
        {
            range = ApiMapping.Window(window, q),
            overview,
            tags,
            years
        });
    }

    [HttpGet("tops/{kind}")]
    public async Task<IActionResult> Tops(
        string kind,
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        [FromQuery] int take = AnalyticsQueries.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var previous = window.Preset == "all" ? null : TimeRangeParser.PreviousWindow(window);
        var tab = kind.ToLowerInvariant();
        var result = tab switch
        {
            "tracks" => await analytics.GetTopTracks(window, previous, q, take, cancellationToken),
            "albums" => await analytics.GetTopAlbums(window, previous, q, take, cancellationToken),
            "artists" => await analytics.GetTopArtists(window, previous, q, take, cancellationToken),
            _ => null
        };
        if (result is null)
        {
            return NotFound(new ApiError("Kind must be artists, tracks, or albums."));
        }

        return Ok(new
        {
            range = ApiMapping.Window(window, q),
            kind = tab,
            take,
            result
        });
    }

    [HttpGet("tags")]
    public async Task<IActionResult> Tags(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        [FromQuery] int take = AnalyticsQueries.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var cloud = await analytics.GetTagCloud(window, q, take, cancellationToken);
        return Ok(new { range = ApiMapping.Window(window, q), take, cloud });
    }

    [HttpGet("tags/{name}")]
    public async Task<IActionResult> TagDetail(
        string name,
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        [FromQuery] int take = AnalyticsQueries.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var detail = await analytics.GetTagDetail(name, window, q, take, cancellationToken);
        if (detail is null)
        {
            return NotFound(new ApiError("Tag not found."));
        }

        return Ok(new { range = ApiMapping.Window(window, q), take, detail });
    }

    [HttpGet("discovery")]
    public async Task<IActionResult> Discovery(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        [FromQuery] int take = AnalyticsQueries.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var result = await analytics.GetDiscoveries(window, q, take, cancellationToken);
        return Ok(new { range = ApiMapping.Window(window, q), take, result });
    }

    [HttpGet("patterns")]
    public async Task<IActionResult> Patterns(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var heatmap = await analytics.GetHeatmap(window, q, cancellationToken);
        var timeOfDay = analytics.GetTimeOfDayBuckets(heatmap);
        var monthly = await analytics.GetMonthlyVolume(window, q, cancellationToken);
        return Ok(new
        {
            range = ApiMapping.Window(window, q),
            heatmap,
            timeOfDay,
            monthly
        });
    }

    [HttpGet("deep-cuts")]
    public async Task<IActionResult> DeepCuts(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        [FromQuery] int n = AnalyticsQueries.DefaultHeavyThreshold,
        [FromQuery] int take = AnalyticsQueries.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        if (n < 2)
        {
            n = AnalyticsQueries.DefaultHeavyThreshold;
        }

        var result = await analytics.GetDeepCuts(window, q, n, take, cancellationToken);
        return Ok(new { range = ApiMapping.Window(window, q), n, take, result });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        [FromQuery] int gap = AnalyticsQueries.DefaultSessionGapMinutes,
        [FromQuery] int take = AnalyticsQueries.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        if (gap <= 0)
        {
            gap = AnalyticsQueries.DefaultSessionGapMinutes;
        }

        var result = await analytics.GetSessions(window, q, gap, take, cancellationToken);
        return Ok(new { range = ApiMapping.Window(window, q), gap, take, result });
    }

    [HttpGet("wrapped/{year:int}")]
    public async Task<IActionResult> Wrapped(int year, [FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (year < 1970)
        {
            return BadRequest(new ApiError("Year must be 1970 or later."));
        }

        var result = await analytics.GetWrapped(year, q, cancellationToken);
        return Ok(result);
    }

    [HttpGet("wrapped/{year:int}/html")]
    public async Task<IActionResult> WrappedHtml(int year, [FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (year < 1970)
        {
            return BadRequest(new ApiError("Year must be 1970 or later."));
        }

        var export = await analytics.GetWrappedHtmlExport(year, q, cancellationToken);
        var html = WrappedHtmlGenerator.Generate(export);
        return File(System.Text.Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", $"wrapped-{year}.html");
    }

    [HttpGet("years")]
    public async Task<IActionResult> Years(CancellationToken cancellationToken) =>
        Ok(new { years = await analytics.GetYears(cancellationToken) });

    [HttpGet("artists/{id:long}")]
    public async Task<IActionResult> Artist(
        long id,
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var detail = await analytics.GetArtistDetail(id, window, q, cancellationToken);
        if (detail is null)
        {
            return NotFound(new ApiError("Artist not found."));
        }

        return Ok(new { range = ApiMapping.Window(window, q), detail });
    }

    [HttpGet("tracks/{id:long}")]
    public async Task<IActionResult> Track(long id, CancellationToken cancellationToken)
    {
        var detail = await analytics.GetTrackDetail(id, cancellationToken);
        return detail is null ? NotFound(new ApiError("Track not found.")) : Ok(detail);
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("tracks/{id:long}/enrich")]
    public async Task<IActionResult> EnrichTrack(
        long id,
        [FromServices] MetadataEnrichmentService enrichment,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await enrichment.EnrichTrackFromMbidAsync(id, cancellationToken);
            return Ok(new EnrichTrackResponse(id, message));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiError(ex.Message));
        }
    }

    [HttpGet("scrobbles")]
    public async Task<IActionResult> Scrobbles(
        [FromQuery] string? range,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? q,
        [FromQuery] int take = AnalyticsQueries.DefaultTake,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var window = ApiTimeRange.Resolve(zone, range, from, to);
        var result = await analytics.GetRecentPlays(window, q, take, page, cancellationToken);
        return Ok(new { range = ApiMapping.Window(window, q), result });
    }
}
