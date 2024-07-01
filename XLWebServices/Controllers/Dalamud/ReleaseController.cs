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
[TypeFilter(typeof(DalamudReleaseDataService.DalamudReleaseDataAvailabilityFilter), IsReusable = true)]
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
        var releases = this.releaseCache.Get()!;
        
        if (string.IsNullOrEmpty(track))
            track = "release";

        if (track == "staging")
            track = "stg";

        if (string.IsNullOrEmpty(appId))
            appId = "goat";

        if (appId != "goat" && appId != "xom" && appId != "helpy")
            return BadRequest("Invalid appId");

        string? keyOverride = null;
        if (releases.DeclarativeAliases.TryGetValue(track, out var aliasTrack))
        {
            keyOverride = releases.DalamudVersions[track].Key;
            track = aliasTrack;
        }
        
        DalamudReleaseDataService.DalamudVersion? resultVersion = null;
        switch (track)
        {
            case "release":
            {
                DownloadsOverTime.WithLabels(appId, bucket == "Canary" ? "Canary" : "Control").Inc();

                if (bucket == "Canary" && releases.DalamudVersions.ContainsKey("canary") && isUseCanary)
                {
                    resultVersion = releases.DalamudVersions["canary"];
                }
                else
                {
                    resultVersion = releases.DalamudVersions["release"];
                }
            }
                break;

            default:
            {
                if (!releases.DalamudVersions.TryGetValue(track, out resultVersion))
                {
                    resultVersion = releases.DalamudVersions["release"];
                    track = "release"; // Normalize track name for stat counting
                }

                DownloadsOverTime.WithLabels(appId, track).Inc();

                // If the version is not applicable for the current game version, fall back to the release version
                // Ideally XL should see this and request stable instead, but here we are.
                if (!resultVersion.IsApplicableForCurrentGameVer.GetValueOrDefault(true))
                {
                    resultVersion = releases.DalamudVersions["release"];
                }
            }
                break;
        }

        // Patch in the key of the aliased version
        if (keyOverride != null)
            resultVersion.Key = keyOverride;

        return new JsonResult(resultVersion);
    }

    [HttpGet]
    public IActionResult Meta()
    {
        return new JsonResult(this.releaseCache.Get()!.DalamudVersions);
    }
    
    [HttpGet]
    public IActionResult Changelog()
    {
        return new JsonResult(this.releaseCache.Get()!.DalamudChangelogs);
    }

    [HttpGet("{kind}/{version}")]
    public async Task<IActionResult> Runtime(string version, string kind)
    {
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
        _logger.LogInformation("Queued Dalamud release refresh is starting");
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