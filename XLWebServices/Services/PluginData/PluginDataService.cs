using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Octokit;
using Tomlyn;
using XLWebServices.Data;
using XLWebServices.Data.Models;

namespace XLWebServices.Services.PluginData;

public class PluginDataService
{
    public class PluginDataAvailabilityFilter : IAsyncActionFilter
    {
        private readonly FallibleService<PluginDataService> _pluginData;

        public PluginDataAvailabilityFilter(FallibleService<PluginDataService> pluginData)
        {
            _pluginData = pluginData;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (_pluginData.HasFailed || !_pluginData.Get()!.HasInitialData)
            {
                context.Result = new StatusCodeResult(503);
                return;
            }

            await next();
        }
    }
    
    private readonly ILogger<PluginDataService> _logger;
    private readonly GitHubService _github;
    private readonly IConfiguration _configuration;
    private readonly FallibleService<RedisService> _redis;
    private readonly DiscordHookService _discord;
    private readonly WsDbContext _dbContext;

    private readonly HttpClient _client;

    // All plugins
    public IReadOnlyList<PluginManifest>? PluginMaster { get; private set; }

    // Per track
    public IReadOnlyDictionary<string, List<PluginManifest>> PluginMastersDip17 { get; private set; }
    
    public string RepoShaDip17 { get; private set; }

    public DateTime LastUpdate { get; private set; }

    public bool HasInitialData { get; private set; } = false;

    public PluginDataService(
        ILogger<PluginDataService> logger,
        GitHubService github,
        IConfiguration configuration,
        FallibleService<RedisService> redis,
        DiscordHookService discord,
        WsDbContext dbContext)
    {
        _logger = logger;
        _github = github;
        _configuration = configuration;
        _redis = redis;
        _discord = discord;
        _dbContext = dbContext;

        _client = new HttpClient();
    }

    public async Task ClearCache()
    {
        _logger.LogInformation("Now clearing the cache");

        try
        {
            /*
            var bannedPlugins =
                await _client.GetFromJsonAsync<BannedPlugin[]>(_configuration["BannedPlugin"]);
            if (bannedPlugins == null)
                throw new Exception("Failed to load banned plugins");
            */

            var pluginMaster = new List<PluginManifest>();

            var d17 = await ClearCacheD17(pluginMaster);
            
            PluginMaster = pluginMaster;
            PluginMastersDip17 = d17.Manifests;
            RepoShaDip17 = d17.Sha;
            LastUpdate = DateTime.Now;
            
            await EnsureDatabaseConsistent();

            _logger.LogInformation("Plugin list updated, {Count} plugins found", this.PluginMaster.Count);
            await _discord.AdminSendSuccess($"Plugin list updated, {this.PluginMaster.Count} plugins loaded\nSHA(D17): {RepoShaDip17}",
                "PluginMaster updated");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to refetch plugins");
            await _discord.AdminSendError($"Failed to reload plugins ({e.Message})", "PluginMaster error");
            throw;
        }
    }

    public async Task PostProcessD17Masters()
    {
        Debug.Assert(PluginMaster != null);
        Debug.Assert(PluginMastersDip17 != null);

        // Patch changelogs into the "main" PluginMaster that has normal and testing versions.
        // This is the only manifest that will have testing changelogs.
        // I don't think this approach scales but I don't care.
        foreach (var plugin in PluginMaster)
        {
            var versionStable = _dbContext.PluginVersions
                .OrderByDescending(x => x.PublishedAt)
                .FirstOrDefault(x => x.Version == plugin.AssemblyVersion && 
                                     x.Dip17Track == Dip17SystemDefine.StableTrack);
            var versionTesting = _dbContext.PluginVersions
                .OrderByDescending(x => x.PublishedAt)
                .FirstOrDefault(x => x.Version == plugin.AssemblyVersion && 
                                     x.Dip17Track != Dip17SystemDefine.StableTrack);
            
            plugin.Changelog = versionStable?.Changelog;
            plugin.TestingChangelog = versionTesting?.Changelog;
        }
        
        // Patch changelogs into individual tracks. These only have the main changelog appropriate for the track.
        foreach (var track in PluginMastersDip17)
        foreach (var plugin in track.Value)
        {
            var version = _dbContext.PluginVersions
                .OrderByDescending(x => x.PublishedAt)
                .FirstOrDefault(x => x.Version == plugin.AssemblyVersion && 
                                     x.Dip17Track == track.Key);
            
            plugin.Changelog = version?.Changelog;
        }

        HasInitialData = true;
    }

    private async Task<(Dictionary<string, List<PluginManifest>> Manifests, string Sha)> ClearCacheD17(List<PluginManifest> pluginMaster)
    {
        var repoOwner = _configuration["GitHub:PluginDistD17:Owner"];
        var repoName = _configuration["GitHub:PluginDistD17:Name"];
        var apiLevel = int.Parse(_configuration["ApiLevel"]!);

        var commit = await _github.Client.Repository.Commit.Get(repoOwner, repoName, "main");
        var sha = commit.Sha;

        var downloadTemplate = _configuration["TemplateDownload"];
        var updateTemplate = _configuration["TemplateUpdate"];

        var bannedPlugins =
            await _client.GetFromJsonAsync<BannedPlugin[]>(_configuration["BannedPlugin"]);
        if (bannedPlugins == null)
            throw new Exception("Failed to load banned plugins");
        
        var stateUrl = $"https://raw.githubusercontent.com/{repoOwner}/{repoName}/{sha}/State.toml";
        var state = Toml.ToModel<Dip17State>(await _client.GetStringAsync(stateUrl));

        var d17Master = new Dictionary<string, List<PluginManifest>>();

        async Task ProcessPluginsInChannel(Dip17State.Channel channel, string channelName)
        {
            foreach (var (pluginName, pluginState) in channel.Plugins)
            {
                var manifest = await GetManifest(repoOwner, repoName, channelName, pluginName, sha);
                
                if (manifest == null)
                    throw new Exception($"Could not fetch manifest for DIP17 plugin: {channelName}/{pluginName}");

                if (manifest.DalamudApiLevel < apiLevel - 2)
                {
                    _logger.LogInformation("{PluginName} too old, api{Level}", manifest.InternalName, manifest.DalamudApiLevel);
                    continue;
                }
                
                var banned = bannedPlugins.LastOrDefault(x => x.Name == manifest.InternalName);
                var isHide = banned != null && manifest.AssemblyVersion <= banned.AssemblyVersion ||
                             manifest.DalamudApiLevel != apiLevel;
                manifest.IsHide = isHide;
                
                // This is NECESSARY here for changelog fallback logic.
                // PlogonController.BuildCommitWorkItemAsync() will take this value and use it as the
                // committed changelog for the plugin if the PR description does not apply.
                manifest.Changelog =
                    pluginState.Changelogs?.FirstOrDefault(x => x.Key == manifest.AssemblyVersion.ToString()).Value?.Changelog;
                manifest.LastUpdate = ((DateTimeOffset)pluginState.TimeBuilt).ToUnixTimeSeconds();
                
                manifest.DownloadLinkInstall = string.Format(downloadTemplate, manifest.InternalName, false, apiLevel, true);
                manifest.DownloadLinkTesting = string.Format(downloadTemplate, manifest.InternalName, true, apiLevel, true);
                manifest.DownloadLinkUpdate = string.Format(updateTemplate, "plugins", manifest.InternalName, apiLevel);
                
                if (!_redis.HasFailed)
                {
                    var dlCount = await _redis.RunFallibleAsync(s => s.GetCount(manifest.InternalName));
                    if (dlCount.HasValue)
                    {
                        manifest.DownloadCount = dlCount.Value;
                    }
                }

                manifest.IsDip17Plugin = true;
                manifest.Dip17Channel = channelName;
                
                d17Master[channelName].Add(manifest);

                if (channelName == "stable")
                {
                    pluginMaster.Add(manifest);
                }
                else if (channelName == "testing-live")
                {
                    // TODO: Changelog for testing versions?
                    
                    var stableVersion = pluginMaster.FirstOrDefault(x => x.InternalName == pluginName);
                    var stableDip17Version = pluginMaster.FirstOrDefault(x => x.InternalName == pluginName);
                    if (stableDip17Version != null)
                        stableVersion = stableDip17Version;
                    
                    if (stableVersion != null)
                    {
                        stableVersion.TestingAssemblyVersion = manifest.AssemblyVersion;
                        stableVersion.IsTestingExclusive = false;
                        stableVersion.IsDip17Plugin = true;
                        stableVersion.Dip17Channel = channelName;
                    }
                    else
                    {
                        manifest.TestingAssemblyVersion = manifest.AssemblyVersion;
                        manifest.IsTestingExclusive = true;
                        pluginMaster.Add(manifest);
                    }
                }
            }
        }

        d17Master["stable"] = new();
        var stableChannel = state.Channels.First(x => x.Key == "stable");
        await ProcessPluginsInChannel(stableChannel.Value, stableChannel.Key);
        
        foreach (var (channelName, channel) in state.Channels.Where(x => x.Key != "stable"))
        {
            d17Master[channelName] = new();
            await ProcessPluginsInChannel(channel, channelName);
        }

        return (d17Master, sha);
    }

    public async Task EnsureDatabaseConsistent()
    {
        if (PluginMaster == null)
            return;
        
        foreach (var manifest in PluginMaster)
        {
            var dbPlugin = _dbContext.Plugins.FirstOrDefault(x => x.InternalName == manifest.InternalName);
            dbPlugin ??= new Plugin();

            dbPlugin.InternalName = manifest.InternalName;

            _dbContext.Plugins.Update(dbPlugin);
        }

        await _dbContext.SaveChangesAsync();
    }
    
    private async Task<(long LastUpdate, string? Changelog)> GetPluginInfo(PluginManifest manifest, RepositoryContent content, string repoOwner, string repoName)
    {
        if (_redis.HasFailed)
            return (0, null);
        
        var cachedInfo = await _redis.Get().GetCachedPlugin(manifest.InternalName, manifest.AssemblyVersion.ToString());
        if (cachedInfo != null)
            return (cachedInfo.LastUpdate, cachedInfo.PrBody);

        var commit = await this.GetCommit(content, repoOwner, repoName);
        var lastUpdate = commit.Commit.Author.Date.ToUnixTimeSeconds();
        if (string.IsNullOrEmpty(manifest.Changelog))
        {
            var desc = await GetPrDescription(commit, repoOwner, repoName);

            if (desc != null && desc.Contains("nofranz"))
                desc = null;

            await _redis.Get().SetCachedPlugin(manifest.InternalName, manifest.AssemblyVersion.ToString(),
                new RedisService.PluginInfo
                {
                    LastUpdate = lastUpdate,
                    PrBody = desc,
                });

            return (lastUpdate, desc);
        }

        await _redis.Get().SetCachedPlugin(manifest.InternalName, manifest.AssemblyVersion.ToString(),
            new RedisService.PluginInfo
            {
                LastUpdate = lastUpdate,
                PrBody = manifest.Changelog,
            });

        return (lastUpdate, manifest.Changelog);
    }

    private async Task<string?> GetPrDescription(GitHubCommit commit, string repoOwner, string repoName)
    {
        var pulls = await _github.Client.Repository.Commit.PullRequests(repoOwner, repoName, commit.Sha);
        return pulls.FirstOrDefault(x => x.Merged)?.Body;
    }

    private async Task<GitHubCommit> GetCommit(RepositoryContent content, string repoOwner, string repoName)
    {
        var commits = await _github.Client.Repository.Commit.GetAll(repoOwner, repoName, new CommitRequest
        {
            Path = content.Path,
        });
        if (commits.Count == 0)
            throw new Exception("Could not find corresponding commit for: " + content.Name);

        return commits[0];
    }

    private async Task<PluginManifest?> GetManifest(string repoOwner, string repoName, string channel, string pluginName, string sha)
    {
        var manifestUrl = $"https://raw.githubusercontent.com/{repoOwner}/{repoName}/{sha}/{channel}/{pluginName}/{pluginName}.json";
        return await _client.GetFromJsonAsync<PluginManifest>(manifestUrl, new JsonSerializerOptions
        {
            AllowTrailingCommas = true, // Haplo's manifest has trailing commas
        });
    }

    private class BannedPlugin
    {
        public string Name { get; set; }
        public Version AssemblyVersion { get; set; }
        public string? Reason { get; set; }
    }
}