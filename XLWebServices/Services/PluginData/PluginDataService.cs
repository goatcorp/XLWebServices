using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Octokit;
using Tomlyn;
using XLWebServices.Data;
using XLWebServices.Data.Models;

namespace XLWebServices.Services.PluginData;

public class PluginDataService
{
    private readonly ILogger<PluginDataService> _logger;
    private readonly GitHubService _github;
    private readonly IConfiguration _configuration;
    private readonly FallibleService<RedisService> _redis;
    private readonly DiscordHookService _discord;
    private readonly WsDbContext _dbContext;

    private readonly HttpClient _client;

    public IReadOnlyList<PluginManifest>? PluginMaster { get; private set; }
    //public IReadOnlyList<PluginManifest>? PluginMasterNoProxy { get; private set; }
    
    public IReadOnlyDictionary<string, List<PluginManifest>> PluginMastersDip17 { get; private set; }

    public string RepoSha { get; private set; }
    
    public string RepoShaDip17 { get; private set; }

    public DateTime LastUpdate { get; private set; }

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
            var repoOwner = _configuration["GitHub:PluginRepository:Owner"];
            var repoName = _configuration["GitHub:PluginRepository:Name"];
            var apiLevel = _configuration["ApiLevel"];

            var commit = await _github.Client.Repository.Commit.Get(repoOwner, repoName, _configuration["PluginRepoBranch"]);
            var sha = commit.Sha;

            var downloadTemplate = _configuration["TemplateDownload"];
            var updateTemplate = _configuration["TemplateUpdate"];

            var bannedPlugins =
                await _client.GetFromJsonAsync<BannedPlugin[]>(_configuration["BannedPlugin"]);
            if (bannedPlugins == null)
                throw new Exception("Failed to load banned plugins");

            var pluginsDir =
                await _github.Client.Repository.Content.GetAllContentsByRef(repoOwner, repoName, "plugins/", sha);
            var testingDir =
                await _github.Client.Repository.Content.GetAllContentsByRef(repoOwner, repoName, "testing/", sha);

            var pluginMaster = new List<PluginManifest>();

            var d17 = await ClearCacheD17(pluginMaster);
            
            foreach (var repositoryContent in pluginsDir)
            {
                if (pluginMaster.Any(x => x.InternalName == repositoryContent.Name))
                    continue;
                
                var manifest = await this.GetManifest(repoOwner, repoName, "plugins", repositoryContent.Name, sha);
                if (manifest == null)
                    throw new Exception($"Could not fetch manifest for release plugin: {repositoryContent.Name}");

                var banned = bannedPlugins.LastOrDefault(x => x.Name == manifest.InternalName);
                var isHide = banned != null && manifest.AssemblyVersion <= banned.AssemblyVersion ||
                             manifest.DalamudApiLevel.ToString() != apiLevel;
                manifest.IsHide = isHide;

                var testingVersion = testingDir.FirstOrDefault(x => x.Name == repositoryContent.Name);
                if (testingVersion != null)
                {
                    var testingManifest = await this.GetManifest(repoOwner, repoName, "testing", repositoryContent.Name, sha);
                    if (testingManifest == null)
                        throw new Exception(
                            $"Plugin had testing version, but could not fetch manifest: {repositoryContent.Name}");

                    manifest.TestingAssemblyVersion = testingManifest.AssemblyVersion;
                }

                manifest.IsTestingExclusive = false;

                var (lastUpdate, changelog) = await this.GetPluginInfo(manifest, repositoryContent, repoOwner, repoName);
                manifest.LastUpdate = lastUpdate;
                manifest.Changelog = changelog;

                if (!_redis.HasFailed)
                {
                    var dlCount = await _redis.RunFallibleAsync(s => s.GetCount(manifest.InternalName));
                    if (dlCount.HasValue)
                    {
                        manifest.DownloadCount = dlCount.Value;
                    }
                }
                
                manifest.DownloadLinkInstall = string.Format(downloadTemplate, manifest.InternalName, false, apiLevel, false);
                manifest.DownloadLinkTesting = string.Format(downloadTemplate, manifest.InternalName, true, apiLevel, false);
                manifest.DownloadLinkUpdate = string.Format(updateTemplate, "plugins", manifest.InternalName, apiLevel);
                
                pluginMaster.Add(manifest);
            }

            foreach (var repositoryContent in testingDir)
            {
                if (pluginMaster.Any(x => x.InternalName == repositoryContent.Name))
                    continue;
                
                var manifest = await this.GetManifest(repoOwner, repoName, "testing", repositoryContent.Name, sha);
                if (manifest == null)
                    throw new Exception($"Could not fetch manifest for testing plugin: {repositoryContent.Name}");

                manifest.TestingAssemblyVersion = manifest.AssemblyVersion;
                manifest.IsTestingExclusive = true;

                var (lastUpdate, changelog) = await this.GetPluginInfo(manifest, repositoryContent, repoOwner, repoName);
                manifest.LastUpdate = lastUpdate;
                manifest.Changelog = changelog;

                if (!_redis.HasFailed)
                {
                    var dlCount = await _redis.RunFallibleAsync(s => s.GetCount(manifest.InternalName));
                    if (dlCount.HasValue)
                    {
                        manifest.DownloadCount = dlCount.Value;
                    }
                }

                manifest.DownloadLinkInstall = string.Format(downloadTemplate, manifest.InternalName, false, apiLevel, false);
                manifest.DownloadLinkTesting = string.Format(downloadTemplate, manifest.InternalName, true, apiLevel, false);
                manifest.DownloadLinkUpdate = string.Format(updateTemplate, manifest.InternalName, "plugins", apiLevel);
                
                pluginMaster.Add(manifest);
            }

            PluginMaster = pluginMaster;
            PluginMastersDip17 = d17.Manifests;
            RepoSha = sha;
            RepoShaDip17 = d17.Sha;
            LastUpdate = DateTime.Now;
            
            EnsureDatabaseConsistent();

            _logger.LogInformation("Plugin list updated, {Count} plugins found", this.PluginMaster.Count);
            await _discord.AdminSendSuccess($"Plugin list updated, {this.PluginMaster.Count} plugins loaded\nSHA: {sha}\nSHA(D17): {RepoShaDip17}",
                "PluginMaster updated");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to refetch plugins");
            await _discord.AdminSendError($"Failed to reload plugins ({e.Message})", "PluginMaster error");
            throw;
        }
    }

    public async Task<(Dictionary<string, List<PluginManifest>> Manifests, string Sha)> ClearCacheD17(List<PluginManifest> pluginMaster)
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