using System.Text.Json;
using System.Text.RegularExpressions;
using Octokit;

namespace XLWebServices.Services.PluginData;

public class PluginDataService
{
    private readonly ILogger<PluginDataService> _logger;
    private readonly GitHubService _github;
    private readonly IConfiguration _configuration;
    private readonly RedisService _redis;
    private readonly DiscordHookService _discord;

    private readonly HttpClient _client;

    public IReadOnlyList<PluginManifest>? PluginMaster { get; private set; }
    public DateTime LastUpdate { get; private set; }

    public PluginDataService(ILogger<PluginDataService> logger, GitHubService github, IConfiguration  configuration, RedisService redis, DiscordHookService discord)
    {
        _logger = logger;
        _github = github;
        _configuration = configuration;
        _redis = redis;
        _discord = discord;

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

            var downloadTemplate = _configuration["TemplateDownload"];
            var updateTemplate = _configuration["TemplateUpdate"];

            var bannedPlugins =
                await _client.GetFromJsonAsync<BannedPlugin[]>(_configuration["BannedPlugin"]);
            if (bannedPlugins == null)
                throw new Exception("Failed to load banned plugins");

            var pluginsDir =
                await _github.Client.Repository.Content.GetAllContents(repoOwner, repoName, "plugins/");
            var testingDir =
                await _github.Client.Repository.Content.GetAllContents(repoOwner, repoName, "testing/");

            var pluginMaster = new List<PluginManifest>();

            foreach (var repositoryContent in pluginsDir)
            {
                var manifest = await this.GetManifest(repositoryContent);
                if (manifest == null)
                    throw new Exception($"Could not fetch manifest for release plugin: {repositoryContent.Name}");

                var banned = bannedPlugins.LastOrDefault(x => x.Name == manifest.InternalName);
                var isHide = banned != null && manifest.AssemblyVersion <= banned.AssemblyVersion ||
                             manifest.DalamudApiLevel.ToString() != apiLevel;
                manifest.IsHide = isHide;

                var testingVersion = testingDir.FirstOrDefault(x => x.Name == repositoryContent.Name);
                if (testingVersion != null)
                {
                    var testingManifest = await this.GetManifest(testingVersion);
                    if (testingManifest == null)
                        throw new Exception(
                            $"Plugin had testing version, but could not fetch manifest: {repositoryContent.Name}");

                    manifest.TestingAssemblyVersion = testingManifest.AssemblyVersion;
                }

                manifest.IsTestingExclusive = false;

                var (lastUpdate, changelog) = await this.GetPluginInfo(manifest, repositoryContent, repoOwner, repoName);
                manifest.LastUpdate = lastUpdate;
                manifest.Changelog = changelog;

                manifest.DownloadCount = await _redis.GetCount(manifest.InternalName);

                manifest.DownloadLinkInstall = string.Format(downloadTemplate, manifest.InternalName, false, apiLevel);
                manifest.DownloadLinkTesting = string.Format(downloadTemplate, manifest.InternalName, true, apiLevel);
                manifest.DownloadLinkUpdate = string.Format(updateTemplate, manifest.InternalName, "plugins", apiLevel);

                pluginMaster.Add(manifest);
            }

            foreach (var repositoryContent in testingDir)
            {
                if (pluginMaster.Any(x => x.InternalName == repositoryContent.Name))
                    continue;

                var manifest = await this.GetManifest(repositoryContent);
                if (manifest == null)
                    throw new Exception($"Could not fetch manifest for testing plugin: {repositoryContent.Name}");

                manifest.TestingAssemblyVersion = manifest.AssemblyVersion;
                manifest.IsTestingExclusive = true;

                var (lastUpdate, changelog) = await this.GetPluginInfo(manifest, repositoryContent, repoOwner, repoName);
                manifest.LastUpdate = lastUpdate;
                manifest.Changelog = changelog;

                manifest.DownloadCount = await _redis.GetCount(manifest.InternalName);

                manifest.DownloadLinkInstall = string.Format(downloadTemplate, manifest.InternalName, false, apiLevel);
                manifest.DownloadLinkTesting = string.Format(downloadTemplate, manifest.InternalName, true, apiLevel);
                manifest.DownloadLinkUpdate = string.Format(updateTemplate, manifest.InternalName, "plugins", apiLevel);

                pluginMaster.Add(manifest);
            }

            PluginMaster = pluginMaster;
            LastUpdate = DateTime.Now;

            _logger.LogInformation("Plugin list updated, {Count} plugins found", this.PluginMaster.Count);
            await this._discord.SendSuccess($"Plugin list updated, {this.PluginMaster.Count} plugins loaded",
                "PluginMaster updated");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to refetch plugins");
            await this._discord.SendError("Failed to reload plugins", "PluginMaster error");
            throw;
        }
    }

    private async Task<(long LastUpdate, string? Changelog)> GetPluginInfo(PluginManifest manifest, RepositoryContent content, string repoOwner, string repoName)
    {
        var cachedInfo = await _redis.GetCachedPlugin(manifest.InternalName, manifest.AssemblyVersion.ToString());
        if (cachedInfo != null)
            return (cachedInfo.LastUpdate, cachedInfo.PrBody);

        var commit = await this.GetCommit(content, repoOwner, repoName);
        var lastUpdate = commit.Commit.Author.Date.ToUnixTimeSeconds();
        if (string.IsNullOrEmpty(manifest.Changelog))
        {
            var desc = await GetPrDescription(commit, repoOwner, repoName);

            if (desc != null && desc.Contains("nofranz"))
                desc = null;

            await _redis.SetCachedPlugin(manifest.InternalName, manifest.AssemblyVersion.ToString(),
                new RedisService.PluginInfo
                {
                    LastUpdate = lastUpdate,
                    PrBody = desc,
                });

            return (lastUpdate, desc);
        }

        await _redis.SetCachedPlugin(manifest.InternalName, manifest.AssemblyVersion.ToString(),
            new RedisService.PluginInfo
            {
                LastUpdate = lastUpdate,
                PrBody = manifest.Changelog,
            });

        return (lastUpdate, manifest.Changelog);
    }

    private async Task<string?> GetPrDescription(GitHubCommit commit, string repoOwner, string repoName)
    {
        var pulls = await _github.Client.Repository.Commit.Pulls(repoOwner, repoName, commit.Sha);
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

    private async Task<PluginManifest?> GetManifest(RepositoryContent pluginFolder)
    {
        var folderUrl = pluginFolder.HtmlUrl.Replace("https://github.com/", "https://raw.githubusercontent.com/").Replace("/tree/", "/");
        var manifestUrl = $"{folderUrl}/{pluginFolder.Name}.json";
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