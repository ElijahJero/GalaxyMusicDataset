using System.Security.Claims;
using GalaxyMusicDataset.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GalaxyMusicDataset.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class ApiRootController(IWebHostEnvironment env) : ControllerBase
{
    public const string Version = "v1";
    public const string DocsPath = "/api-docs";

    [AllowAnonymous]
    [HttpGet("")]
    [HttpGet("v1")]
    public ActionResult<ApiCatalogResponse> Catalog()
    {
        return Ok(new ApiCatalogResponse(
            "Galaxy Music REST API",
            Version,
            DocsPath,
            "Send the key as `Authorization: Bearer gmk_...` or `X-Api-Key: gmk_...`. Create keys on Settings while signed in as admin.",
            [ApiKeyScopes.Read, ApiKeyScopes.Write],
            new Dictionary<string, string>
            {
                ["documentation"] = "/api/docs",
                ["me"] = "/api/v1/me",
                ["overview"] = "/api/v1/overview",
                ["tops"] = "/api/v1/tops/{artists|tracks|albums}",
                ["tags"] = "/api/v1/tags",
                ["discovery"] = "/api/v1/discovery",
                ["patterns"] = "/api/v1/patterns",
                ["deepCuts"] = "/api/v1/deep-cuts",
                ["sessions"] = "/api/v1/sessions",
                ["wrapped"] = "/api/v1/wrapped/{year}",
                ["artists"] = "/api/v1/artists/{id}",
                ["tracks"] = "/api/v1/tracks/{id}",
                ["pendingAudio"] = "/api/v1/tracks/pending-audio",
                ["audioProfile"] = "/api/v1/tracks/{id}/audio-profile",
                ["audioProfiles"] = "/api/v1/audio-profiles",
                ["audio"] = "/api/v1/audio",
                ["scrobbles"] = "/api/v1/scrobbles",
                ["library"] = "/api/v1/library",
                ["lookups"] = "/api/v1/lookups",
                ["review"] = "/api/v1/review",
                ["status"] = "/api/v1/status",
                ["jobs"] = "/api/v1/jobs/{sync|backfill|pause|resume|retry-lookups|seed}",
                ["settings"] = "/api/v1/settings",
                ["apiKeys"] = "/api/v1/keys"
            }));
    }

    [AllowAnonymous]
    [HttpGet("docs")]
    [Produces("text/markdown")]
    public IActionResult Markdown()
    {
        foreach (var path in DocPaths())
        {
            if (System.IO.File.Exists(path))
            {
                return PhysicalFile(path, "text/markdown; charset=utf-8");
            }
        }

        return NotFound(new ApiError("API documentation file was not found on this server."));
    }

    [Authorize(Policy = ApiPolicies.Read)]
    [HttpGet("v1/me")]
    public ActionResult<ApiMeResponse> Me()
    {
        return Ok(new ApiMeResponse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            User.Identity?.Name ?? "",
            User.FindFirstValue("api_key_prefix") ?? "",
            User.FindAll(ApiKeyScopes.ClaimType).Select(c => c.Value).ToList()));
    }

    private IEnumerable<string> DocPaths()
    {
        yield return Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "docs", "API.md"));
        yield return Path.Combine(AppContext.BaseDirectory, "docs", "API.md");
        yield return Path.Combine(env.ContentRootPath, "docs", "API.md");
    }
}
