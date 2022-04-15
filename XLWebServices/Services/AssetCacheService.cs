using System.Net.Http.Headers;
using Newtonsoft.Json;
using Octokit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace XLWebServices.Services;

public class AssetCacheService
{
    private readonly IConfiguration config;
    private readonly FileCacheService cache;
    private readonly GitHubService github;
    private readonly ILogger<AssetCacheService> logger;

    private int? AssetVersion { get; set; }
    public IReadOnlyList<Asset>? Assets { get; private set; }
    public AssetResponse? Response { get; private set; }

    public AssetCacheService(IConfiguration config, FileCacheService cache, GitHubService github, ILogger<AssetCacheService> logger)
    {
        this.config = config;
        this.cache = cache;
        this.github = github;
        this.logger = logger;
    }

    public async Task ClearCache()
    {
        var repoOwner = this.config["GitHub:AssetRepository:Owner"];
        var repoName = this.config["GitHub:AssetRepository:Name"];

        var commit = await this.github.Client.Repository.Commit.Get(repoOwner, repoName, "master");
        var sha = commit.Sha;

        var assetsInfoText = await this.github.Client.Repository.Content.GetRawContentByRef(repoOwner, repoName, "asset.json", sha);
        var assetsInfo = JsonSerializer.Deserialize<AssetResponse>(assetsInfoText);

        if (assetsInfo == null)
            throw new Exception("Couldn't fetch assets.");

        this.cache.ClearCategory(FileCacheService.CachedFile.FileCategory.Asset);

        foreach (var asset in assetsInfo.Assets)
        {
            if (asset.Url.Contains("github"))
            {
                var fileUrl = asset.Url.Replace("master", sha);

                var file = await this.cache.CacheFile(asset.FileName, assetsInfo.Version.ToString(), fileUrl,
                    FileCacheService.CachedFile.FileCategory.Asset);
                //asset.Url = $"{this.config["HostedUrl"]}/File/Get/{file.Id}";
            }
        }

        this.AssetVersion = assetsInfo.Version;
        this.Assets = assetsInfo.Assets;
        this.Response = assetsInfo;

        this.logger.LogInformation($"Correctly refreshed assets for {AssetVersion.Value}");
    }

    public class Asset
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public string Hash { get; set; }
    }

    public class AssetResponse
    {
        public int Version { get; set; }
        public List<Asset> Assets { get; set; }
    }
}