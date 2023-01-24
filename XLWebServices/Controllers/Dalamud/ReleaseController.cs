using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[EnableCors("GithubAccess")]
[Route("Dalamud/Release/[action]")]
public class ReleaseController : ControllerBase
{
    private readonly FallibleService<DalamudReleaseDataService> releaseCache;
    private readonly FileCacheService cache;
    private readonly IConfiguration configuration;
    private readonly DiscordHookService discordHookService;

    private static readonly Counter DownloadsOverTime =
        Metrics.CreateCounter("xl_dalamud_startups", "Dalamud Unique Startups", "AppID");

    private static bool isUseCanary = false;

    public ReleaseController(FallibleService<DalamudReleaseDataService> releaseCache, FileCacheService cache,
        IConfiguration configuration, DiscordHookService discordHookService)
    {
        this.releaseCache = releaseCache;
        this.cache = cache;
        this.configuration = configuration;
        this.discordHookService = discordHookService;
    }

    [HttpGet]
    public IActionResult VersionInfo([FromQuery] string? track = "", [FromQuery] string? appId = "", [FromQuery] string? bucket = "Control")
    {
        if (this.releaseCache.HasFailed && this.releaseCache.Get()?.DalamudVersions == null)
            return StatusCode(500, "Precondition failed");
        
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
                
                if (bucket == "Canary" && this.releaseCache.Get()!.DalamudVersions.ContainsKey("canary") && isUseCanary)
                    return new JsonResult(this.releaseCache.Get()!.DalamudVersions["canary"]);
                
                return new JsonResult(this.releaseCache.Get()!.DalamudVersions["release"]);
            }

            case "staging":
                return new JsonResult(this.releaseCache.Get()!.DalamudVersions["stg"]);

            default:
            {
                if (!this.releaseCache.Get()!.DalamudVersions.TryGetValue(track, out var release))
                    return new JsonResult(this.releaseCache.Get()!.DalamudVersions["release"]);

                return new JsonResult(release);
            }
        }
    }

    [HttpGet]
    public IActionResult Meta()
    {
        return new JsonResult(this.releaseCache.Get()!.DalamudVersions);
    }

    [HttpGet("{kind}/{version}")]
    public async Task<IActionResult> Runtime(string version, string kind)
    {
        if (this.releaseCache.HasFailed && this.releaseCache.Get()?.DalamudVersions == null)
            return StatusCode(500, "Precondition failed");
        
        if (this.releaseCache.Get()!.DalamudVersions.All(x => x.Value.RuntimeVersion != version) && version != "5.0.6")
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
    public async Task<IActionResult> SetUseCanary([FromQuery] string key, [FromQuery] bool useCanary)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        isUseCanary = useCanary;
        return this.Ok(useCanary);
    }

    [HttpPost]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        await this.releaseCache.Get()!.ClearCache();

        return Ok(this.releaseCache.HasFailed);
    }
}