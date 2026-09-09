using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiPolicies.Read)]
[Route("api/v1")]
[Produces("application/json")]
public sealed class StatusApiController(
    AggregationStatusService statusService,
    AggregationCoordinator coordinator,
    IServiceScopeFactory scopes,
    SampleDataSeeder seeder) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<StatusResponse>> Status(CancellationToken cancellationToken)
    {
        var dto = await statusService.GetStatusAsync(cancellationToken);
        return Ok(ApiMapping.Status(dto));
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("jobs/sync")]
    public ActionResult<JobQueuedResponse> Sync()
    {
        coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.SyncIncremental));
        return Accepted(new JobQueuedResponse(true, "sync"));
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("jobs/backfill")]
    public ActionResult<JobQueuedResponse> Backfill([FromBody] BackfillRequest? body)
    {
        var days = body?.Days is > 0 and <= 365 ? body.Days : 14;
        coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.Backfill, days));
        return Accepted(new JobQueuedResponse(true, $"backfill:{days}"));
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("jobs/pause")]
    public async Task<ActionResult<JobQueuedResponse>> Pause(CancellationToken cancellationToken)
    {
        await coordinator.SetPausedAsync(scopes, true, cancellationToken);
        return Ok(new JobQueuedResponse(true, "pause"));
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("jobs/resume")]
    public async Task<ActionResult<JobQueuedResponse>> Resume(CancellationToken cancellationToken)
    {
        await coordinator.SetPausedAsync(scopes, false, cancellationToken);
        return Ok(new JobQueuedResponse(true, "resume"));
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("jobs/retry-lookups")]
    public ActionResult<JobQueuedResponse> RetryLookups()
    {
        coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.RetryFailedLookups));
        return Accepted(new JobQueuedResponse(true, "retry-lookups"));
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("jobs/seed")]
    public async Task<ActionResult<JobQueuedResponse>> Seed(CancellationToken cancellationToken)
    {
        await seeder.SeedIfEmptyAsync(SampleDataSeeder.DefaultSample(), cancellationToken);
        return Ok(new JobQueuedResponse(true, "seed"));
    }
}
