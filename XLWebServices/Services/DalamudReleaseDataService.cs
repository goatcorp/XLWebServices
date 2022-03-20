using System.Net.Http.Headers;
using Octokit;

namespace XLWebServices.Services;

public class DalamudReleaseDataService
{
    private readonly IConfiguration config;
    private readonly FileCacheService cache;
    private readonly ILogger<DalamudReleaseDataService> logger;
    private readonly GitHubService github;
    private readonly DiscordHookService discord;

    public DalamudVersion ReleaseVersion { get; private set; }
    public DalamudVersion StagingVersion { get; private set; }

    public IReadOnlyList<DalamudChangelog> DalamudChangelogs { get; private set; }

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

        var repo =
            $"https://raw.githubusercontent.com/{this.config["GitHub:DistribRepository:Owner"]}/{this.config["GitHub:DistribRepository:Name"]}";

        var releaseFile = $"{repo}/master/version";
        var stgFile = $"{repo}/master/stg/version";
        var releaseZip = $"{repo}/master/latest.zip";
        var stgZip = $"{repo}/master/stg/latest.zip";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        };

        ReleaseVersion = await client.GetFromJsonAsync<DalamudVersion>(releaseFile);
        StagingVersion = await client.GetFromJsonAsync<DalamudVersion>(stgFile);

        if (ReleaseVersion == null || StagingVersion == null)
        {
            throw new Exception("Failed to get release data");
        }

        var releaseCache = await this.cache.CacheFile("latest.zip", ReleaseVersion.AssemblyVersion, releaseZip,
            FileCacheService.CachedFile.FileCategory.Dalamud);
        ReleaseVersion.DownloadUrl = $"{this.config["HostedUrl"]}/File/Get/{releaseCache.FileId}";
        ReleaseVersion.Track = "release";
        ReleaseVersion.Changelog = DalamudChangelogs.FirstOrDefault(x => x.Version == ReleaseVersion.AssemblyVersion);

        var stgCache = await this.cache.CacheFile("staging.zip", StagingVersion.AssemblyVersion, stgZip,
            FileCacheService.CachedFile.FileCategory.Dalamud);
        StagingVersion.DownloadUrl = $"{this.config["HostedUrl"]}/File/Get/{stgCache.FileId}";
        StagingVersion.Track = "staging";

        await this.discord.SendSuccess($"Release: {ReleaseVersion.AssemblyVersion}({ReleaseVersion?.Changelog?.Changes.Count} Changes)\nStaging: {StagingVersion.AssemblyVersion}", "Dalamud releases updated!");

        this.logger.LogInformation($"Correctly refreshed Dalamud releases");
    }

        private async Task BuildDalamudChangelogs()
    {
        this.logger.LogInformation("Now getting Dalamud changelogs");

        var repoOwner = this.config["GitHub:DalamudRepository:Owner"];
        var repoName = this.config["GitHub:DalamudRepository:Name"];
        var tags = await this.github.Client.Repository.GetAllTags(repoOwner, repoName, new ApiOptions{PageSize = 100});

        var orderedTags = tags.Select(async x => (x, await this.github.Client.Repository.Commit.Get(repoOwner, repoName, x.Commit.Sha)))
            .Select(t => t.Result)
            .OrderByDescending(x => x.Item2.Commit.Author.Date).ToList();

        var changelogs = new List<DalamudChangelog>();

        for (var i = 0; i < orderedTags.Count; i++)
        {
            var currentTag = orderedTags[i];
            if (i + 1 >= orderedTags.Count)
                break;

            var nextTag = orderedTags[i + 1];

            var changelog = new DalamudChangelog
            {
                Version = currentTag.x.Name,
                Date = currentTag.Item2.Commit.Author.Date.DateTime,
                Changes = new List<DalamudChangelog.DalamudChangelogChange>()
            };

            var diff = await this.github.Client.Repository.Commit.Compare(repoOwner, repoName, nextTag.x.Commit.Sha, currentTag.x.Commit.Sha);
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

    public class DalamudVersion
    {
        public string Key { get; set; }

        public string Track { get; set; }

        public string AssemblyVersion { get; set; }

        public string RuntimeVersion { get; set; }

        public bool RuntimeRequired { get; set; }

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