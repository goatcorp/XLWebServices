using System.Diagnostics;
using Discord;
using Microsoft.AspNetCore.Mvc;
using XLWebServices.Data;
using XLWebServices.Data.Models;
using XLWebServices.Services;
using XLWebServices.Services.JobQueue;
using XLWebServices.Services.PluginData;

namespace XLWebServices.Controllers;

[ApiController]
[Route("/Plogon/[action]")]
public class PlogonController : ControllerBase
{
    private readonly FallibleService<RedisService> _redis;
    private readonly FallibleService<PluginDataService> _data;
    private readonly WsDbContext _dbContext;
    private readonly GitHubService _github;
    private readonly DiscordHookService _discord;
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<PlogonController> _logger;
    private readonly ConfigMasterService _config;

    private const string MsgIdsKey = "PLOGONSTREAM_MSGS-";
    private const string ChangelogKey = "PLOGONSTREAM_CHANGELOG-";
    
    // we'll lose track if we ever restart during a commit, oops
    private static List<StagedPluginInfo> _stagedPlugins = new();
    private static bool _currentlyUnstaging = new();

    public PlogonController(
        FallibleService<RedisService> redis,
        FallibleService<PluginDataService> data,
        WsDbContext dbContext,
        GitHubService github,
        DiscordHookService discord,
        IBackgroundTaskQueue queue,
        ILogger<PlogonController> logger,
        ConfigMasterService config)
    {
        _redis = redis;
        _data = data;
        _dbContext = dbContext;
        _github = github;
        _discord = discord;
        _queue = queue;
        _logger = logger;
        _config = config;
    }
    
    [HttpPost]
    public async Task<IActionResult> RegisterMessageId(
        [FromQuery] string key,
        [FromQuery] string prNumber,
        [FromQuery] string messageId)
    {
        if (!CheckAuth(key))
            return Unauthorized();

        await _redis.Get()!.Database.ListRightPushAsync(MsgIdsKey + prNumber, messageId);
        
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetMessageIds([FromQuery] string prNumber)
    {
        var ids = await _redis.Get()!.Database.ListRangeAsync(MsgIdsKey + prNumber);
        if (ids == null)
            return NotFound();
        
        var idList = ids.Select(redisValue => redisValue.ToString()).ToList();

        return new JsonResult(idList);
    }

    public class StagedPluginInfo
    {
        public string InternalName { get; set; }
        public string Version { get; set; }
        public string Dip17Track { get; set; }
        public int? PrNumber { get; set; }
        public string? Changelog { get; set; }
        public bool IsInitialRelease { get; set; }
        public int DiffLinesAdded { get; set; }
        public int DiffLinesRemoved { get; set; }
    }
    
    [HttpPost]
    public async Task<IActionResult> CommitStagedPlugins()
    {
        if (!CheckAuthHeader())
            return Unauthorized();
        
        var toCommit = _stagedPlugins.ToList();
        _stagedPlugins.Clear();

        await _queue.QueueBackgroundWorkItemAsync((token, provider) => BuildCommitWorkItemAsync(token, provider, toCommit));

        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> StagePluginBuild(
        [FromBody] StagedPluginInfo payload)
    {
        if (!CheckAuthHeader())
            return Unauthorized();

        if (_stagedPlugins.Any(x => x.InternalName == payload.InternalName && x.Dip17Track == payload.Dip17Track))
            return Ok();
        
        _stagedPlugins.Add(payload);

        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> RegisterVersionPrNumber(
        [FromQuery] string key,
        [FromQuery] string internalName,
        [FromQuery] string version,
        [FromQuery] string prNumber)
    {
        if (!CheckAuth(key))
            return Unauthorized();
        
        await _redis.Get()!.Database.StringSetAsync(ChangelogKey + $"{internalName}-{version}", prNumber);
        
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetVersionChangelog([FromQuery] string internalName, [FromQuery] string version)
    {
        var changelog = await _redis.Get()!.Database.StringGetAsync(ChangelogKey + $"{internalName}-{version}");
        if (!changelog.HasValue)
            return NotFound();

        return Content(changelog.ToString());
    }

    public class PlogonStats
    {
        public TimeSpan MeanMergeTimeNew { get; set; }
        public TimeSpan MeanMergeTimeUpdate { get; set; }
    }
    
    [HttpGet]
    public PlogonStats Stats()
    {
        const int windowSize = 16;
        var stats = new PlogonStats();
        
        var pluginsWithMergeTime = _dbContext.PluginVersions.Where(x => x.TimeToMerge != null);
        
        var lastXNew = pluginsWithMergeTime
            .Where(x => x.IsInitialRelease == true)
            .TakeLast(windowSize);
        var lastXNewSum = lastXNew.Sum(x => x.TimeToMerge!.Value.TotalHours);
        if (lastXNew.Any())
            stats.MeanMergeTimeNew = TimeSpan.FromHours(lastXNewSum / lastXNew.Count());
        
        var lastXUpdated = pluginsWithMergeTime
            .Where(x => x.IsInitialRelease == false)
            .TakeLast(windowSize);
        var lastXUpdatedSum = lastXUpdated.Sum(x => x.TimeToMerge!.Value.TotalHours);
        if (lastXUpdated.Any())
            stats.MeanMergeTimeUpdate = TimeSpan.FromHours(lastXUpdatedSum / lastXUpdated.Count());

        return stats;
    }
    
    private async Task<(string Name, string Icon, TimeSpan? TimeToMerge)?> GetPrInfo(int? prNum)
    {
        if (prNum == null)
            return null;
        
        var pr = await _github.Client.Repository.PullRequest.Get(
            _config.Dip17RepoOwner,
            _config.Dip17RepoName,
            prNum.Value);

        if (pr == null)
            return null;

        TimeSpan? timeToMerge = null;
        if (pr.ClosedAt != null)
        {
            timeToMerge = pr.ClosedAt - pr.CreatedAt;
        }
        else
        {
            _logger.LogError("Dip17 PR {PrNum} wasn't closed when we were committing it???", prNum);
        }
        
        return (pr.User.Name ?? pr.User.Login, pr.User.AvatarUrl, timeToMerge);
    }

    private string GetDip17IconUrl(string track, string internalName)
    {
        return
            $"https://raw.githubusercontent.com/{_config.Dip17DistRepoOwner}/{_config.Dip17DistRepoName}/main/{track}/{internalName}/images/icon.png";
    }
    
    private async ValueTask BuildCommitWorkItemAsync(CancellationToken token, IServiceProvider provider, List<StagedPluginInfo> staged)
    {
        _logger.LogInformation("Queued plogon commit is starting");
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var data = _data.Get()!;
        
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WsDbContext>();

        try
        {
            await data.ClearCache();

            foreach (var pluginInfo in staged)
            {
                var manifest = data.PluginMastersDip17[pluginInfo.Dip17Track]
                    .FirstOrDefault(x => x.InternalName == pluginInfo.InternalName);

                if (manifest == null)
                {
                    _logger.LogError("Couldn't find manifest for {InternalName}", pluginInfo.InternalName);
                    continue;
                }

                // ?? ("Unknown Author", "https://goatcorp.github.io/icons/gon.png");
                var prInfo = await GetPrInfo(pluginInfo.PrNumber);

                var shallExplicitlyHideChangelog = pluginInfo.Changelog != null && pluginInfo.Changelog.StartsWith(Dip17SystemDefine.ChangelogMarkerHide);
                if (shallExplicitlyHideChangelog)
                    pluginInfo.Changelog = pluginInfo.Changelog![Dip17SystemDefine.ChangelogMarkerHide.Length..];

                if (pluginInfo.Changelog != null)
                {
                    pluginInfo.Changelog = pluginInfo.Changelog.TrimStart().TrimEnd();
                }

                var dbPlugin  = db.Plugins.FirstOrDefault(x => x.InternalName == pluginInfo.InternalName);
                if (dbPlugin != null)
                {
                    var version = new PluginVersion
                    {
                        Plugin = dbPlugin,
                        Dip17Track = pluginInfo.Dip17Track,
                        Version = pluginInfo.Version,
                        PrNumber = pluginInfo.PrNumber,
                        Changelog = pluginInfo.Changelog,
                        PublishedAt = DateTime.Now,
                        PublishedBy = prInfo?.Name,
                        IsHidden = shallExplicitlyHideChangelog,
                        DiffLinesAdded = pluginInfo.DiffLinesAdded,
                        DiffLinesRemoved = pluginInfo.DiffLinesRemoved,
                        IsInitialRelease = pluginInfo.IsInitialRelease,
                        TimeToMerge = prInfo?.TimeToMerge,
                    };
                    dbPlugin.VersionHistory.Add(version);
                }
                else
                {
                    _logger.LogError("Plugin '{InternalName}' not found in db!!", pluginInfo.InternalName);
                }
                
                // Send discord notification
                if (pluginInfo.Changelog == null || !shallExplicitlyHideChangelog)
                {
                    var isOtherRepo = pluginInfo.Dip17Track != Dip17SystemDefine.MainTrack;
                    
                    var embed = new EmbedBuilder()
                        .WithTitle($"{manifest.Name} (v{pluginInfo.Version})")
                        .WithAuthor(prInfo?.Name, prInfo?.Icon)
                        .WithDescription(string.IsNullOrEmpty(pluginInfo.Changelog) ? "This dev didn't write a changelog." : pluginInfo.Changelog)
                        .WithThumbnailUrl(GetDip17IconUrl(pluginInfo.Dip17Track, pluginInfo.InternalName))
                        .Build();

                    await _discord.SendRelease(embed, isOtherRepo);
                }
            }

            await db.SaveChangesAsync(token);

            _logger.LogInformation("Committed {NumPlogons} in {Secs}s", staged.Count, stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not process plogon commit job");
        }
    }

    private bool CheckAuthHeader()
    {
        return CheckAuth(Request.Headers["X-XL-Key"]);
    }

    private bool CheckAuth(string? key)
    {
        return !string.IsNullOrWhiteSpace(key) && key == _config.PlogonApiKey;
    }
}