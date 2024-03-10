using System.Text.Json;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using XLWebServices.Data;
using XLWebServices.Data.Models;
using XLWebServices.Services;
using XLWebServices.Services.PluginData;

namespace XLWebServices.Controllers;

[ApiController]
[EnableCors("GithubAccess")]
[Route("[controller]/[action]")]
public class PluginController : ControllerBase
{
    private readonly ILogger<PluginController> logger;
    private readonly FallibleService<RedisService> redis;
    private readonly IConfiguration configuration;
    private readonly FallibleService<PluginDataService> pluginData;
    private readonly FallibleService<DalamudReleaseDataService> releaseData;
    private readonly FileCacheService cache;
    private readonly WsDbContext _dbContext;

    private static bool UseFileProxy = true;

    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_plugindl", "XIVLauncher Plugin Downloads", "Name", "Testing");
    private static readonly Counter EndorsementsOverTime = Metrics.CreateCounter("xl_pluginendorse", "XIVLauncher Plugin Endorsements", "Name");

    private const string RedisCumulativeKey = "XLPluginDlCumulative";

    public PluginController(ILogger<PluginController> logger, FallibleService<RedisService> redis, IConfiguration configuration, FallibleService<PluginDataService> pluginData, FallibleService<DalamudReleaseDataService> dalamudReleaseData, FileCacheService cache, WsDbContext dbContext)
    {
        this.logger = logger;
        this.redis = redis;
        this.configuration = configuration;
        this.pluginData = pluginData;
        this.releaseData = dalamudReleaseData;
        this.cache = cache;
        _dbContext = dbContext;
    }

    [HttpGet("{internalName}")]
    public async Task<IActionResult> Download(string internalName, [FromQuery(Name = "isTesting")] bool isTesting = false, [FromQuery(Name = "isDip17")] bool isDip17 = false)
    {
        if (this.pluginData.HasFailed&& this.pluginData.Get()?.PluginMaster == null)
            return StatusCode(500, "Precondition failed");
        
        var masterList = this.pluginData.Get()!.PluginMaster;

        var manifest = masterList!.FirstOrDefault(x => x.InternalName == internalName);
        if (manifest == null)
            return BadRequest("Invalid plugin");

        DownloadsOverTime.WithLabels(internalName.ToLower(), isTesting.ToString()).Inc();

        if (!this.redis.HasFailed)
        {
            await this.redis.Get()!.IncrementCount(internalName);
            await this.redis.Get()!.IncrementCount(RedisCumulativeKey);
        }
        
        if (isDip17)
        {
            const string githubPath = "https://raw.githubusercontent.com/goatcorp/PluginDistD17/{0}/{1}/{2}/latest.zip";
            var folder = isTesting ? "testing-live" : "stable";
            var version = isTesting && manifest.TestingAssemblyVersion != null ? manifest.TestingAssemblyVersion : manifest.AssemblyVersion;
            var cachedFile = await this.cache.CacheFile(internalName, $"{version}-{folder}-{this.pluginData.Get()!.RepoShaDip17}",
                string.Format(githubPath, this.pluginData.Get()!.RepoShaDip17, folder, internalName), FileCacheService.CachedFile.FileCategory.Plugin);

            return new RedirectResult($"{this.configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
        }
        else
        {
            const string githubPath = "https://raw.githubusercontent.com/goatcorp/DalamudPlugins/{0}/{1}/{2}/latest.zip";
            var folder = isTesting ? "testing" : "plugins";
            var version = isTesting && manifest.TestingAssemblyVersion != null ? manifest.TestingAssemblyVersion : manifest.AssemblyVersion;
            var cachedFile = await this.cache.CacheFile(internalName, $"{version}-{folder}-{this.pluginData.Get()!.RepoSha}",
                string.Format(githubPath, this.pluginData.Get()!.RepoSha, folder, internalName), FileCacheService.CachedFile.FileCategory.Plugin);

            return new RedirectResult($"{this.configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
        }
    }

    [HttpPost("{internalName}")]
    [EnableRateLimiting("limitip")]
    public async Task<IActionResult> Endorse(string internalName) {
        if (this.redis.HasFailed && this.pluginData.Get()?.PluginMaster == null)
            return StatusCode(500, "Precondition failed");
        
        var masterList = this.pluginData.Get()!.PluginMaster;

        var manifest = masterList!.FirstOrDefault(x => x.InternalName == internalName);
        if (manifest == null)
            return BadRequest("Invalid plugin");
        
        EndorsementsOverTime.WithLabels(internalName.ToLower()).Inc();
        var endCount = await this.redis.Get()!.IncrementEndCount(internalName);

        return Content(endCount.ToString());
    }
    
    [HttpGet]
    public async Task<IActionResult> DownloadCounts()
    {
        if (this.pluginData.HasFailed || this.redis.HasFailed)
            return StatusCode(500, "Precondition failed");
        
        var counts = new Dictionary<string, long>();
        foreach (var plugin in this.pluginData.Get()!.PluginMaster!)
        {
            counts.Add(plugin.InternalName, await this.redis.Get()!.GetCount(plugin.InternalName));
        }

        return new JsonResult(counts);
    }

    [HttpGet]
    public IActionResult PluginMaster([FromQuery] bool proxy = true, [FromQuery] int minApiLevel = 0, [FromQuery(Name = "track")] string? dip17Track = null)
    {
        if (this.pluginData.HasFailed && this.pluginData.Get()?.PluginMaster == null)
            return StatusCode(500, "Precondition failed");

        IReadOnlyList<PluginManifest>? pluginMaster;
        if (!string.IsNullOrEmpty(dip17Track))
        {
            if (!this.pluginData.Get()!.PluginMastersDip17.TryGetValue(dip17Track, out var trackMaster))
                return NotFound("Not found track");
            pluginMaster = trackMaster;
        }
        else
        {
            pluginMaster = this.pluginData.Get()!.PluginMaster;
        }

        pluginMaster ??= Array.Empty<PluginManifest>();
        if (minApiLevel > 0)
        {
            pluginMaster = pluginMaster.Where(manifest => manifest.DalamudApiLevel >= minApiLevel).ToArray();
        }

        return Content(JsonSerializer.Serialize(pluginMaster), "application/json");
    }

    [HttpGet("{internalName}")]
    public IActionResult Plugin(string internalName, [FromQuery(Name = "track")] string? dip17Track = null)
    {
        if (this.pluginData.HasFailed)
            return StatusCode(500, "Precondition failed");

        var master = this.pluginData.Get()!.PluginMaster;
        
        if (!string.IsNullOrEmpty(dip17Track))
        {
            if (!this.pluginData.Get()!.PluginMastersDip17.TryGetValue(dip17Track, out var trackMaster))
                return NotFound("Not found track");

            master = trackMaster;
        }
        
        var plugin = master!.FirstOrDefault(x => x.InternalName == internalName);
        if (plugin == null)
            return NotFound("Not found plugin");

        return Content(JsonSerializer.Serialize(plugin), "application/json");
    }

    public class HistoryResponse
    {
        public List<PluginVersion> Versions { get; set; }
    }

    [HttpGet("{internalName}")]
    public IActionResult History(string internalName, [FromQuery(Name = "track")] string? dip17Track = null)
    {
        if (this.pluginData.HasFailed)
            return StatusCode(500, "Precondition failed");
        
        if (string.IsNullOrEmpty(dip17Track))
        {
            dip17Track = Dip17SystemDefine.MainTrack;
        }
        
        var dbPlugin = _dbContext.Plugins.Include(x => x.VersionHistory).FirstOrDefault(x => x.InternalName == internalName);
        if (dbPlugin == null)
            return NotFound("Not found plugin");
        
        return new OkObjectResult(new HistoryResponse
        {
            Versions = dbPlugin.VersionHistory.Where(x => x.Dip17Track == dip17Track).OrderByDescending(x => x.PublishedAt).ToList(),
        });
    }

    [DisableCors]
    [HttpPost]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        return Ok(this.pluginData.HasFailed);
    }

    [DisableCors]
    [HttpPost]
    public async Task<IActionResult> SetUseProxy([FromQuery] string key, [FromQuery] bool useProxy)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        UseFileProxy = useProxy;
        return this.Ok(UseFileProxy);
    }

    [HttpGet]
    public async Task<IActionResult> Meta()
    {
        if (this.pluginData.HasFailed || this.redis.HasFailed)
            return StatusCode(500, "Precondition failed");
        
        return new JsonResult(new PluginMeta
        {
            NumPlugins = this.pluginData.Get()!.PluginMaster!.Count,
            LastUpdate = this.pluginData.Get()!.LastUpdate,
            CumulativeDownloads = await this.redis.Get()!.GetCount(RedisCumulativeKey),
            Sha = this.pluginData.Get()!.RepoSha,
            Dip17Sha = this.pluginData.Get()!.RepoShaDip17,
        });
    }

    public class PluginMeta
    {
        public int NumPlugins { get; init; }
        public long CumulativeDownloads { get; init; }
        public DateTime LastUpdate { get; init; }
        public string Sha { get; init; }
        public string Dip17Sha { get; init; }
    }
}