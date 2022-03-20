using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Dalamud/Release/")]
public class ReleaseController : ControllerBase
{
    private readonly DalamudReleaseDataService releaseCache;
    private readonly IConfiguration configuration;

    public ReleaseController(DalamudReleaseDataService releaseCache, IConfiguration configuration)
    {
        this.releaseCache = releaseCache;
        this.configuration = configuration;
    }

    [HttpGet("VersionInfo/{track?}")]
    public IActionResult Get(string? track)
    {
        if (string.IsNullOrEmpty(track))
            track = "release";

        switch (track)
        {
            case "release":
                return new JsonResult(this.releaseCache.ReleaseVersion);

            case "stg":
                return new JsonResult(this.releaseCache.StagingVersion);

            default:
                return new BadRequestResult();
        }
    }

    [HttpGet("[action]")]
    public IActionResult Meta()
    {
        return new JsonResult(new Dictionary<string, DalamudReleaseDataService.DalamudVersion>
        {
            { "release", this.releaseCache.ReleaseVersion },
            { "stg", this.releaseCache.StagingVersion }
        });
    }

    [HttpGet("[action]")]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        await this.releaseCache.ClearCache();

        return Ok();
    }
}