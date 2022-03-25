using System.Net.Http.Headers;

namespace XLWebServices.Services;

public class AssetCacheService
{
    private readonly IConfiguration config;
    private readonly FileCacheService cache;
    private readonly ILogger<AssetCacheService> logger;

    private int? AssetVersion { get; set; }
    public IReadOnlyList<Asset>? Assets { get; private set; }
    public AssetResponse? Response { get; private set; }

    public AssetCacheService(IConfiguration config, FileCacheService cache, ILogger<AssetCacheService> logger)
    {
        this.config = config;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task ClearCache()
    {
        var repo =
            $"https://raw.githubusercontent.com/{this.config["GitHub:AssetRepository:Owner"]}/{this.config["GitHub:AssetRepository:Name"]}";

        var assetsFile = $"{repo}/master/asset.json?={DateTime.UtcNow:yyyyMMddHHmmss}";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        };

        var assetsInfo = await client.GetFromJsonAsync<AssetResponse>(assetsFile);

        if (assetsInfo == null)
            throw new Exception("Couldn't fetch assets.");

        foreach (var asset in assetsInfo.Assets)
        {
            var file = await this.cache.CacheFile(asset.FileName, assetsInfo.Version.ToString(), asset.Url,
                FileCacheService.CachedFile.FileCategory.Asset);
            asset.Url = $"{this.config["HostedUrl"]}/File/Get/{file.Id}";
        }

        if (this.AssetVersion.HasValue)
            this.cache.ClearCategory(FileCacheService.CachedFile.FileCategory.Asset, this.AssetVersion.Value.ToString());

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