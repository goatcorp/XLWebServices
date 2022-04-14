using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Dalamud/Release/[action]")]
public class ReleaseController : ControllerBase
{
    private readonly DalamudReleaseDataService releaseCache;
    private readonly IConfiguration configuration;

    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_dalamud_startups", "Dalamud Unique Startups", "AppID");

    public ReleaseController(DalamudReleaseDataService releaseCache, IConfiguration configuration)
    {
        this.releaseCache = releaseCache;
        this.configuration = configuration;
    }

    [HttpGet]
    public IActionResult VersionInfo([FromQuery] string? track = "", [FromQuery] string? appId = "")
    {
        if (string.IsNullOrEmpty(track))
            track = "release";

        if (string.IsNullOrEmpty(appId))
            appId = "goat";

        if (appId != "goat" && appId != "xom")
            return BadRequest("Invalid appId");

        switch (track)
        {
            case "release":
            {
                DownloadsOverTime.WithLabels(appId).Inc();
                return new JsonResult(this.releaseCache.DalamudVersions["release"]);
            }

            case "staging":
                return new JsonResult(this.releaseCache.DalamudVersions["stg"]);

            default:
            {
                if (!this.releaseCache.DalamudVersions.TryGetValue(track, out var release))
                    return this.BadRequest("Invalid track");

                return new JsonResult(release);
            }
        }
    }

    [HttpGet]
    public IActionResult Meta()
    {
        return new JsonResult(this.releaseCache.DalamudVersions);
    }

    [HttpPost]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        await this.releaseCache.ClearCache();

        return Ok();
    }
}