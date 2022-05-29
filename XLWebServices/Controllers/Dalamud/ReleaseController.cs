using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Dalamud/Release/[action]")]
public class ReleaseController : ControllerBase
{
    private readonly DalamudReleaseDataService releaseCache;
    private readonly FileCacheService cache;
    private readonly IConfiguration configuration;

    private static readonly Counter DownloadsOverTime =
        Metrics.CreateCounter("xl_dalamud_startups", "Dalamud Unique Startups", "AppID");

    public ReleaseController(DalamudReleaseDataService releaseCache, FileCacheService cache,
        IConfiguration configuration)
    {
        this.releaseCache = releaseCache;
        this.cache = cache;
        this.configuration = configuration;
    }

    [HttpGet]
    public IActionResult VersionInfo([FromQuery] string? track = "", [FromQuery] string? appId = "", [FromQuery] string? bucket = "Control")
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
                
                if (bucket == "Canary" && this.releaseCache.DalamudVersions.ContainsKey("canary"))
                    return new JsonResult(this.releaseCache.DalamudVersions["canary"]);
                
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

    [HttpGet("{kind}/{version}")]
    public async Task<IActionResult> Runtime(string version, string kind)
    {
        if (this.releaseCache.DalamudVersions.All(x => x.Value.RuntimeVersion != version))
            return this.BadRequest("Invalid version");

        switch (kind)
        {
            case "WindowsDesktop":
            {
                var cachedFile = await this.cache.CacheFile("DNRWindows", $"{version}",
                    $"https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/{version}/windowsdesktop-runtime-{version}-win-x64.zip",
                    FileCacheService.CachedFile.FileCategory.Runtime);
                return new RedirectResult($"{this.configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
            }
            case "DotNet":
            {
                var cachedFile = await this.cache.CacheFile("DNR", $"{version}",
                    $"https://dotnetcli.azureedge.net/dotnet/Runtime/{version}/dotnet-runtime-{version}-win-x64.zip",
                    FileCacheService.CachedFile.FileCategory.Runtime);
                return new RedirectResult($"{this.configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
            }
            case "Hashes":
            {
                var cachedFile = await this.cache.CacheFile("DNRHashes", $"{version}", string.Format(this.configuration["RuntimeHashesUrl"], version),
                    FileCacheService.CachedFile.FileCategory.Runtime);
                return new RedirectResult($"{this.configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
            }
            default:
                return this.BadRequest("Invalid kind");
        }
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