using System.Diagnostics;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;
using XLWebServices.Services.JobQueue;

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
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<ReleaseController> _logger;

    private static readonly Counter DownloadsOverTime =
        Metrics.CreateCounter("xl_dalamud_startups", "Dalamud Unique Startups", "AppID", "Track");

    private static bool isUseCanary = false;

    public ReleaseController(FallibleService<DalamudReleaseDataService> releaseCache, FileCacheService cache,
        IConfiguration configuration, DiscordHookService discordHookService, IBackgroundTaskQueue queue, ILogger<ReleaseController> logger)
    {
        this.releaseCache = releaseCache;
        this.cache = cache;
        this.configuration = configuration;
        this.discordHookService = discordHookService;
        _queue = queue;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult VersionInfo([FromQuery] string? track = "", [FromQuery] string? appId = "", [FromQuery] string? bucket = "Control")
    {
        if (this.releaseCache.HasFailed && this.releaseCache.Get()?.DalamudVersions == null)
            return StatusCode(500, "Precondition failed");
        
        if (string.IsNullOrEmpty(track))
            track = "release";

        if (track == "staging")
            track = "stg";

        if (string.IsNullOrEmpty(appId))
            appId = "goat";

        if (appId != "goat" && appId != "xom" && appId != "helpy")
            return BadRequest("Invalid appId");

        switch (track)
        {
            case "release":
            {
                DownloadsOverTime.WithLabels(appId, bucket == "Canary" ? "Canary" : "Control").Inc();
                
                if (bucket == "Canary" && this.releaseCache.Get()!.DalamudVersions.ContainsKey("canary") && isUseCanary)
                    return new JsonResult(this.releaseCache.Get()!.DalamudVersions["canary"]);
                
                return new JsonResult(this.releaseCache.Get()!.DalamudVersions["release"]);
            }

            default:
            {
                if (!this.releaseCache.Get()!.DalamudVersions.TryGetValue(track, out var release))
                    return new JsonResult(this.releaseCache.Get()!.DalamudVersions["release"]);

                DownloadsOverTime.WithLabels(appId, track).Inc();
                return new JsonResult(release);
            }
        }
    }

    [HttpGet]
    public IActionResult Meta()
    {
        if (this.releaseCache.HasFailed && this.releaseCache.Get()?.DalamudVersions == null)
            return StatusCode(500, "Precondition failed");
        
        return new JsonResult(this.releaseCache.Get()!.DalamudVersions);
    }
    
    [HttpGet]
    public IActionResult Changelog()
    {
        if (this.releaseCache.HasFailed)
            return StatusCode(500, "Precondition failed");
        
        return new JsonResult(this.releaseCache.Get()!.DalamudChangelogs);
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

        await _queue.QueueBackgroundWorkItemAsync(BuildClearCacheWorkItemAsync);

        return Ok();
    }
    
    private async ValueTask BuildClearCacheWorkItemAsync(CancellationToken token, IServiceProvider _)
    {
        _logger.LogInformation("Queued plogon commit is starting");
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            await this.releaseCache.Get()!.ClearCache();

            _logger.LogInformation("ClearCache() in {Secs}s", stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not process job");
        }
    }
}