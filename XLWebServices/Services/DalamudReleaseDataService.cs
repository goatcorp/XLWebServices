using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Octokit;

namespace XLWebServices.Services;

public class DalamudReleaseDataService
{
    private readonly IConfiguration config;
    private readonly FileCacheService cache;
    private readonly ILogger<DalamudReleaseDataService> logger;
    private readonly GitHubService github;
    private readonly DiscordHookService discord;

    public IReadOnlyList<DalamudChangelog> DalamudChangelogs { get; private set; }

    public IReadOnlyDictionary<string, DalamudVersion> DalamudVersions { get; private set; }

    public DalamudReleaseDataService(IConfiguration config, FileCacheService cache,
        ILogger<DalamudReleaseDataService> logger, GitHubService github, DiscordHookService discord)
    {
        this.config = config;
        this.cache = cache;
        this.logger = logger;
        this.github = github;
        this.discord = discord;
    }

    public async Task ClearCache()
    {
        try
        {
            await this.BuildDalamudChangelogs();
        }
        catch (Exception e)
        {
            this.logger.LogError(e, "Failed to build Dalamud changelogs");
            throw;
        }

        var repoBase =
            $"https://raw.githubusercontent.com/{this.config["GitHub:DistribRepository:Owner"]}/{this.config["GitHub:DistribRepository:Name"]}";

        var repoOwner = this.config["GitHub:DistribRepository:Owner"];
        var repoName = this.config["GitHub:DistribRepository:Name"];

        var commit = await this.github.Client.Repository.Commit.Get(repoOwner, repoName, "main");
        var sha = commit.Sha;

        // Get tree
        var tree = await this.github.Client.Repository.Content.GetAllContentsByRef(repoOwner, repoName, sha);

        if (tree == null)
            throw new Exception($"Repo {repoName} not found");

        var releasesDict = new Dictionary<string, DalamudVersion>();

        string? currentGameVer = null;
        try
        {
            currentGameVer = await GetCurrentGameVer();
            
            if (currentGameVer == null)
                logger.LogError("Thaliak returned null for gamever");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't fetch gamever from Thaliak");
        }

        var release = await GetDalamudRelease(string.Empty, repoOwner, repoName, sha, currentGameVer);
        release.Changelog = DalamudChangelogs.FirstOrDefault(x => x.Version == release.AssemblyVersion);

        releasesDict.Add("release", release);

        foreach (var content in tree)
        {
            if (content.Type != ContentType.Dir || content.Name == ".github" || content.Name == "runtimehashes")
                continue;

            var tempRelease = await GetDalamudRelease(content.Name, repoOwner, repoName, sha, currentGameVer);
            

            if (content.Name == "canary")
            {
                if (Version.Parse(release.AssemblyVersion) < Version.Parse(tempRelease.AssemblyVersion))
                {
                    tempRelease.Changelog = release.Changelog;
                }
                else
                {
                    this.logger.LogInformation("Canary version is older than release version, skipping({Release} >= {Canary})", release.AssemblyVersion, tempRelease.AssemblyVersion);
                    continue;
                }
            }

            releasesDict.Add(content.Name, tempRelease);
        }
        
        var discordMessage =
            $"Release: {release.AssemblyVersion}({release.Changelog?.Changes.Count} Changes)\n";

        foreach (var version in releasesDict)
        {
            if (version.Key == "release")
                continue;

            discordMessage += $"{version.Key}: {version.Value.AssemblyVersion}\n";
        }

        this.DalamudVersions = releasesDict;

        await this.discord.SendSuccess(discordMessage,
            "Dalamud releases updated!");

        this.logger.LogInformation($"Correctly refreshed Dalamud releases");
    }

    private async Task<DalamudVersion> GetDalamudRelease(string trackName, string repoOwner, string repoName, string sha, string? currentGameVer)
    {
        if (!string.IsNullOrEmpty(trackName))
            trackName = $"{trackName}/";

        var repoBase =
            $"https://raw.githubusercontent.com/{repoOwner}/{repoName}";

        var releaseJson = JsonSerializer.Deserialize<DalamudVersion>(Encoding.UTF8.GetString(
            await this.github.Client.Repository.Content.GetRawContentByRef(repoOwner, repoName, $"{trackName}version", sha)));
        var releaseUrl = $"{repoBase}/{sha}/{trackName}latest.zip";

        if (releaseJson == null)
            throw new Exception($"Failed to get release data for {trackName}");

        var releaseCache = await this.cache.CacheFile("latest.zip", $"{releaseJson.AssemblyVersion}-{trackName}", releaseUrl,
            FileCacheService.CachedFile.FileCategory.Dalamud);

        releaseJson.Track = string.IsNullOrEmpty(trackName) ? "release" : trackName;
        releaseJson.DownloadUrl = $"{this.config["HostedUrl"]}/File/Get/{releaseCache.Id}";

        if (currentGameVer != null)
            releaseJson.IsApplicableForCurrentGameVer = releaseJson.SupportedGameVer == currentGameVer;
        
        return releaseJson;
    }

    private async Task BuildDalamudChangelogs()
    {
        this.logger.LogInformation("Now getting Dalamud changelogs");

        var repoOwner = this.config["GitHub:DalamudRepository:Owner"];
        var repoName = this.config["GitHub:DalamudRepository:Name"];
        var tags = await this.github.Client.Repository.GetAllTags(repoOwner, repoName,
            new ApiOptions { PageSize = 100 });

        var orderedTags = tags.Select(async x =>
                (x, await this.github.Client.Repository.Commit.Get(repoOwner, repoName, x.Commit.Sha)))
            .Select(t => t.Result)
            .OrderByDescending(x => x.Item2.Commit.Author.Date).ToList();

        var changelogs = new List<DalamudChangelog>();

        var maxChangelogs = Math.Min(15, orderedTags.Count);

        for (var i = 0; i < maxChangelogs; i++)
        {
            var currentTag = orderedTags[i];
            if (i + 1 >= maxChangelogs)
                break;

            var nextTag = orderedTags[i + 1];

            var changelog = new DalamudChangelog
            {
                Version = currentTag.x.Name,
                Date = currentTag.Item2.Commit.Author.Date.DateTime,
                Changes = new List<DalamudChangelog.DalamudChangelogChange>()
            };

            var diff = await this.github.Client.Repository.Commit.Compare(repoOwner, repoName, nextTag.x.Commit.Sha,
                currentTag.x.Commit.Sha);
            foreach (var diffCommit in diff.Commits)
            {
                // Exclude build commits
                if (diffCommit.Commit.Message.StartsWith("build:"))
                    continue;

                // Exclude PR merges
                if (diffCommit.Commit.Message.StartsWith("Merge pull request"))
                    continue;

                // Exclude merge commits
                if (diffCommit.Commit.Message.StartsWith("Merge branch"))
                    continue;

                // Get first line
                var firstLine = diffCommit.Commit.Message.Split('\r', '\n')[0];

                changelog.Changes.Add(new DalamudChangelog.DalamudChangelogChange
                {
                    Author = diffCommit.Commit.Author.Name,
                    Message = firstLine,
                    Sha = diffCommit.Sha,
                    Date = diffCommit.Commit.Author.Date.DateTime,
                });
            }

            changelogs.Add(changelog);
        }

        this.DalamudChangelogs = changelogs;

        this.logger.LogInformation("Got {Count} Dalamud changelogs", this.DalamudChangelogs.Count);
    }
    
    private class ThaliakGqlModel
    {
        public class Data
        {
            [JsonPropertyName("repository")]
            public Repository? Repository { get; set; }
        }

        public class LatestVersion
        {
            [JsonPropertyName("versionString")]
            public string? VersionString { get; set; }
        }

        public class Repository
        {
            [JsonPropertyName("latestVersion")]
            public LatestVersion? LatestVersion { get; set; }
        }

        public class Root
        {
            [JsonPropertyName("data")]
            public Data? Data { get; set; }
        }
    }

    private async Task<string?> GetCurrentGameVer()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XLWebServices", "1.0.0"));


        var thaliakGqlMessage =
            new HttpRequestMessage(HttpMethod.Post, "https://thaliak.xiv.dev/graphql/2022-08-14");
        thaliakGqlMessage.Content =
            new StringContent(
                "{\"query\":\"query GetLatestGameVersion {  repository(slug:\\\"4e9a232b\\\") {    latestVersion {      versionString    }  }}\"}",
                Encoding.UTF8, "application/json");

        var thaliakResponse = await client.SendAsync(thaliakGqlMessage);
        thaliakResponse.EnsureSuccessStatusCode();
        var thaliakJson = await thaliakResponse.Content.ReadFromJsonAsync<ThaliakGqlModel.Root>();

        return thaliakJson?.Data?.Repository?.LatestVersion?.VersionString;
    }

    public class DalamudVersion
    {
        public string Key { get; set; }

        public string Track { get; set; }

        public string AssemblyVersion { get; set; }

        public string RuntimeVersion { get; set; }

        public bool RuntimeRequired { get; set; }

        public string SupportedGameVer { get; set; }
        
        // null means unknown
        public bool? IsApplicableForCurrentGameVer { get; set; }

        public DalamudChangelog? Changelog { get; set; }

        public string DownloadUrl { get; set; }
    }

    public class DalamudChangelog
    {
        public DateTime Date { get; set; }
        public string Version { get; set; }
        public List<DalamudChangelogChange> Changes { get; set; }

        public class DalamudChangelogChange
        {
            public string Message { get; set; }
            public string Author { get; set; }
            public string Sha { get; set; }
            public DateTime Date { get; set; }
        }
    }
}