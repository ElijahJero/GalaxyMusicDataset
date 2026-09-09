using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiPolicies.Read)]
[Route("api/v1")]
[Produces("application/json")]
public sealed class SettingsApiController(
    IOptionsMonitor<LastFmOptions> lastFm,
    IOptionsMonitor<DiscogsOptions> discogs,
    IOptionsMonitor<TheAudioDbOptions> audioDb,
    IOptionsMonitor<MusicBrainzOptions> musicBrainz,
    IOptionsMonitor<AggregationOptions> aggregation,
    UserSettingsStore store,
    ApiKeyStore keys) : ControllerBase
{
    [HttpGet("settings")]
    public ActionResult<AggregationSettingsResponse> Get() => Ok(ReadSettings());

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPut("settings")]
    public async Task<ActionResult<AggregationSettingsResponse>> Put(
        [FromBody] UserSettingsModel input,
        CancellationToken cancellationToken)
    {
        var existing = new StoredSecrets(
            lastFm.CurrentValue.ApiKey,
            discogs.CurrentValue.Token,
            audioDb.CurrentValue.ApiKey);
        await store.SaveAsync(input, existing, cancellationToken);
        return Ok(ReadSettings());
    }

    [HttpGet("keys")]
    public ActionResult<IReadOnlyList<ApiKeyInfo>> ListKeys() => Ok(keys.List());

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpPost("keys")]
    public ActionResult<CreatedApiKeyResponse> CreateKey([FromBody] CreateApiKeyRequest body)
    {
        try
        {
            var created = keys.Create(body.Name, body.Read, body.Write);
            return StatusCode(StatusCodes.Status201Created, new CreatedApiKeyResponse(
                created.Info.Id,
                created.Info.Name,
                created.Info.Prefix,
                created.Info.Scopes,
                created.Info.CreatedAt,
                created.Token,
                "Store this token now. It will not be shown again."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError(ex.Message));
        }
    }

    [Authorize(Policy = ApiPolicies.Write)]
    [HttpDelete("keys/{id}")]
    public IActionResult RevokeKey(string id) =>
        keys.Revoke(id) ? NoContent() : NotFound(new ApiError("API key not found."));

    private AggregationSettingsResponse ReadSettings()
    {
        var lf = lastFm.CurrentValue;
        var d = discogs.CurrentValue;
        var a = audioDb.CurrentValue;
        var mb = musicBrainz.CurrentValue;
        var agg = aggregation.CurrentValue;
        return new AggregationSettingsResponse(
            lf.Username,
            !string.IsNullOrWhiteSpace(lf.ApiKey),
            !string.IsNullOrWhiteSpace(d.Token),
            !string.IsNullOrWhiteSpace(a.ApiKey),
            mb.Contact,
            string.Equals(mb.ResolvedBaseUrl, MusicBrainzOptions.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase)
                ? ""
                : mb.ResolvedBaseUrl,
            string.Equals(mb.ResolvedCoverArtBaseUrl, MusicBrainzOptions.DefaultCoverArtBaseUrl, StringComparison.OrdinalIgnoreCase)
                ? ""
                : mb.ResolvedCoverArtBaseUrl,
            mb.MinIntervalMs,
            agg.EnableMusicBrainz,
            agg.EnableLastFmTrackInfo,
            agg.EnableDiscogs,
            agg.EnableTheAudioDb,
            agg.EnableVocaDb,
            agg.EnableUtaiteDb,
            agg.EnableTouhouDb,
            agg.IncrementalIntervalMinutes,
            agg.SeedSampleData);
    }
}
