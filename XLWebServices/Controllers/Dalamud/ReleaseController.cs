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
                return new JsonResult(this.releaseCache.ReleaseVersion);
            }

            case "staging":
            case "stg":
                return new JsonResult(this.releaseCache.StagingVersion);

            default:
                return this.BadRequest("Invalid track");
        }
    }

    [HttpGet]
    public IActionResult Meta()
    {
        return new JsonResult(new Dictionary<string, DalamudReleaseDataService.DalamudVersion>
        {
            { "release", this.releaseCache.ReleaseVersion },
            { "stg", this.releaseCache.StagingVersion }
        });
    }

    [HttpGet]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        await this.releaseCache.ClearCache();

        return Ok();
    }
}