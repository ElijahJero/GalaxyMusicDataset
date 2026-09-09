using GalaxyMusicDataset.Services.Api;
using GalaxyMusicDataset.Services.Audio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiPolicies.Read)]
[Route("api/v1")]
[Produces("application/json")]
public sealed class AudioApiController(AudioProfileService audio) : ControllerBase
{
    [HttpGet("tracks/pending-audio")]
    public async Task<IActionResult> Pending(
        [FromQuery] int take = AudioProfileService.DefaultPendingTake,
        CancellationToken cancellationToken = default)
    {
        var items = await audio.ListPendingAsync(take, cancellationToken);
        return Ok(new { take, count = items.Count, items });
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPut("tracks/{id:long}/audio-profile")]
    public async Task<IActionResult> Upsert(long id, [FromBody] AudioProfileWriteRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiError("A JSON audio profile body is required."));
        }

        try
        {
            var profile = await audio.UpsertAsync(id, request, cancellationToken);
            return Ok(new { trackId = id, profile });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return StatusCode(status, new ApiError(ex.Message));
        }
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPut("audio-profiles")]
    public async Task<IActionResult> UpsertByNames([FromBody] AudioProfileWriteRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new ApiError("A JSON audio profile body is required."));
        }

        try
        {
            var profile = await audio.UpsertByNamesAsync(request.Artist, request.Title, request, cancellationToken);
            return Ok(new { profile });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message.Contains("No track matches", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return StatusCode(status, new ApiError(ex.Message));
        }
    }
}
