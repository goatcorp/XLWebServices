using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;
using XLWebServices.Services.PluginData;

namespace XLWebServices.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class PluginController : ControllerBase
{
    private readonly ILogger<PluginController> logger;
    private readonly RedisService redis;
    private readonly IConfiguration configuration;
    private readonly PluginDataService pluginData;
    private readonly DalamudReleaseDataService releaseData;
    private readonly FileCacheService cache;

    private static bool UseFileProxy = true;

    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_plugindl", "XIVLauncher Plugin Downloads", "Name", "Testing");

    private const string RedisCumulativeKey = "XLPluginDlCumulative";

    public PluginController(ILogger<PluginController> logger, RedisService redis, IConfiguration configuration, PluginDataService pluginData, DalamudReleaseDataService dalamudReleaseData, FileCacheService cache)
    {
        this.logger = logger;
        this.redis = redis;
        this.configuration = configuration;
        this.pluginData = pluginData;
        this.releaseData = dalamudReleaseData;
        this.cache = cache;
    }

    [HttpGet("{internalName}")]
    public async Task<IActionResult> Download(string internalName, [FromQuery(Name = "isTesting")] bool isTesting = false, [FromQuery(Name = "isDip17")] bool isDip17 = false)
    {
        var masterList = this.pluginData.PluginMaster;
        //if (!UseFileProxy)
        //    masterList = this.pluginData.PluginMasterNoProxy;

        var manifest = masterList!.FirstOrDefault(x => x.InternalName == internalName);
        if (manifest == null)
            return BadRequest("Invalid plugin");

        DownloadsOverTime.WithLabels(internalName.ToLower(), isTesting.ToString()).Inc();

        await this.redis.IncrementCount(internalName);
        await this.redis.IncrementCount(RedisCumulativeKey);

        if (isDip17)
        {
            const string githubPath = "https://raw.githubusercontent.com/goatcorp/PluginDistD17/{0}/{1}/{2}/latest.zip";
            var folder = isTesting ? "testing-live" : "stable";
            var version = isTesting && manifest.TestingAssemblyVersion != null ? manifest.TestingAssemblyVersion : manifest.AssemblyVersion;
            var cachedFile = await this.cache.CacheFile(internalName, $"{version}-{folder}-{this.pluginData.RepoShaDip17}",
                string.Format(githubPath, this.pluginData.RepoShaDip17, folder, internalName), FileCacheService.CachedFile.FileCategory.Plugin);

            return new RedirectResult($"{this.configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
        }
        else
        {
            const string githubPath = "https://raw.githubusercontent.com/goatcorp/DalamudPlugins/{0}/{1}/{2}/latest.zip";
            var folder = isTesting ? "testing" : "plugins";
            var version = isTesting && manifest.TestingAssemblyVersion != null ? manifest.TestingAssemblyVersion : manifest.AssemblyVersion;
            var cachedFile = await this.cache.CacheFile(internalName, $"{version}-{folder}-{this.pluginData.RepoSha}",
                string.Format(githubPath, this.pluginData.RepoSha, folder, internalName), FileCacheService.CachedFile.FileCategory.Plugin);

            return new RedirectResult($"{this.configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
        }
    }

    [HttpGet]
    public async Task<Dictionary<string, long>> DownloadCounts()
    {
        var counts = new Dictionary<string, long>();
        foreach (var plugin in this.pluginData.PluginMaster!)
        {
            counts.Add(plugin.InternalName, await this.redis.GetCount(plugin.InternalName));
        }

        return counts;
    }

    [HttpGet]
    public IActionResult PluginMaster([FromQuery] bool proxy = true)
    {
        //if (proxy && UseFileProxy)
        //{
            return Content(JsonSerializer.Serialize(this.pluginData.PluginMaster, new JsonSerializerOptions
            {
                WriteIndented = true,
            }), "application/json");
        //}

        /*
        return Content(JsonSerializer.Serialize(this.pluginData.PluginMasterNoProxy, new JsonSerializerOptions
        {
            WriteIndented = true,
        }), "application/json");
        */
    }

    [HttpGet("{internalName}")]
    public IActionResult Plugin(string internalName)
    {
        var plugin = this.pluginData.PluginMaster!.FirstOrDefault(x => x.InternalName == internalName);
        if (plugin == null)
            return BadRequest("Invalid plugin");

        return Content(JsonSerializer.Serialize(plugin, new JsonSerializerOptions
        {
            WriteIndented = true,
        }), "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        await this.pluginData.ClearCache();
        this.cache.ClearCategory(FileCacheService.CachedFile.FileCategory.Plugin);

        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> SetUseProxy([FromQuery] string key, [FromQuery] bool useProxy)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        UseFileProxy = useProxy;
        return this.Ok(UseFileProxy);
    }

    [HttpGet]
    public async Task<PluginMeta> Meta()
    {
        return new PluginMeta
        {
            NumPlugins = this.pluginData.PluginMaster!.Count,
            LastUpdate = this.pluginData.LastUpdate,
            CumulativeDownloads = await this.redis.GetCount(RedisCumulativeKey),
            Sha = this.pluginData.RepoSha,
        };
    }

    [HttpGet]
    public IReadOnlyList<DalamudReleaseDataService.DalamudChangelog> CoreChangelog()
    {
        return this.releaseData.DalamudChangelogs;
    }

    public class PluginMeta
    {
        public int NumPlugins { get; init; }
        public long CumulativeDownloads { get; init; }
        public DateTime LastUpdate { get; init; }
        public string Sha { get; init; }
    }
}