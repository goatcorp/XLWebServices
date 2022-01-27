using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class PluginController : ControllerBase
{
    private readonly ILogger<PluginController> _logger;
    private readonly RedisService _redis;
    private readonly IConfiguration _configuration;

    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_plugindl", "XIVLauncher Plugin Downloads", "Name", "Testing");

    private static string? _pluginMasterRaw;
    private static List<PluginMasterEntry>? _plugins;
    private static DateTime _lastUpdate;

    private const string RedisCumulativeKey = "XLPluginDlCumulative";

    public PluginController(ILogger<PluginController> logger, RedisService redis, IConfiguration configuration)
    {
        _logger = logger;
        _redis = redis;
        _configuration = configuration;
    }

    [HttpGet("{internalName}")]
    public async Task<IActionResult> Download(string internalName, [FromQuery(Name = "branch")] string branch = "master", [FromQuery(Name = "isTesting")] bool isTesting = false)
    {
        if (_plugins == null)
            await SetupPluginMasterAsync();

        if (_plugins.All(x => x.InternalName != internalName))
            return BadRequest("Invalid plugin");

        DownloadsOverTime.WithLabels(internalName.ToLower(), isTesting.ToString()).Inc();

        await _redis.Increment(internalName);
        await _redis.Increment(RedisCumulativeKey);

        const string githubPath = "https://raw.githubusercontent.com/goatcorp/DalamudPlugins/{0}/{1}/{2}/latest.zip";
        var baseUrl = isTesting ? "testing" : "plugins";
        return new RedirectResult(string.Format(githubPath, branch, baseUrl, internalName));
    }

    [HttpGet]
    public async Task<Dictionary<string, long>> GetDownloadCounts()
    {
        if (_pluginMasterRaw == null)
            await SetupPluginMasterAsync();

        var counts = new Dictionary<string, long>();
        foreach (var plugin in _plugins)
        {
            counts.Add(plugin.InternalName, await _redis.Get(plugin.InternalName));
        }

        return counts;
    }

    [HttpGet]
    public async Task<IActionResult> GetPluginMaster()
    {
        if (_pluginMasterRaw == null)
            await SetupPluginMasterAsync();

        return Content(_pluginMasterRaw, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != _configuration["CacheClearKey"])
            return BadRequest();

        await SetupPluginMasterAsync();

        return Ok();
    }

    [HttpGet]
    public async Task<PluginMeta> Meta()
    {
        if (_plugins == null)
            throw new Exception("Plugin master not loaded");

        return new PluginMeta
        {
            NumPlugins = _plugins.Count,
            LastUpdate = _lastUpdate,
            CumulativeDownloads = await _redis.Get(RedisCumulativeKey),
        };
    }

    public class PluginMeta
    {
        public int NumPlugins { get; init; }
        public long CumulativeDownloads { get; init; }
        public DateTime LastUpdate { get; init; }
    }

    private async Task SetupPluginMasterAsync()
    {
        using var client = new HttpClient();
        var pluginMaster = await client.GetStringAsync(_configuration["PluginMaster"]);

        _pluginMasterRaw = pluginMaster;
        _plugins = JsonSerializer.Deserialize<List<PluginMasterEntry>>(pluginMaster);
        _lastUpdate = DateTime.Now;
    }

    private class PluginMasterEntry
    {
        public string InternalName { get; set; }
    }
}