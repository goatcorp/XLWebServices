using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace XLWebServices.Services;

public class FileCacheService
{
    private readonly ILogger<FileCacheService> logger;
    private readonly ConcurrentDictionary<string, CachedFile> cached = new();
    private readonly ConcurrentDictionary<string, CachedFile> cachedById = new();
    private readonly HttpClient client;
    private readonly DirectoryInfo cacheDirectory;

    public long CacheSize => this.cached.Sum(x => x.Value.Length);

    public Dictionary<CachedFile.FileCategory, int> CountPerCategory => this.cached.GroupBy(x => x.Value.Category).ToDictionary(x => x.Key, x => x.Count());

    public FileCacheService(IConfiguration configuration, ILogger<FileCacheService> logger)
    {
        this.logger = logger;
        this.client = new HttpClient();
        this.client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        };

        this.cacheDirectory = new DirectoryInfo(configuration.GetValue<string>("FileCacheDirectory"));
        if (!this.cacheDirectory.Exists)
            this.cacheDirectory.Create();
    }

    public async Task<CachedFile> CacheFile(string fileName, string cacheKey, string url, CachedFile.FileCategory category)
    {
        var key = $"{fileName}-{cacheKey}";

        if (this.cached.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var file = await GetFile(url, cacheKey, category);

        if (!this.cached.TryAdd(key, file))
        {
            throw new Exception($"Failed to add file to cache!!!! {fileName} {file.Id} {cacheKey} {url} {category}");
        }

        if (!this.cachedById.TryAdd(file.Id, file))
        {
            this.logger.LogWarning($"Failed to add file to cachedById!!!! Duplicate?\n\t{fileName}\n\t{file.Id}\n\t{cacheKey}\n\t{url}\n\t{category}");
        }

        this.logger.LogInformation($"Now cached: {this.cached.Count}, {this.cachedById.Count}, {url}, {cacheKey}, {file.Id}");

        return file;
    }

    public CachedFile? GetCachedFile(string id)
    {
        if (this.cachedById.TryGetValue(id, out var cachedFile))
            return cachedFile;

        return null;
    }

    public void ClearCategory(CachedFile.FileCategory category, string? cacheKey = null)
    {
        var filesToRemove = this.cached.Where(x => x.Value.Category == category);
        if (cacheKey != null)
        {
            filesToRemove = filesToRemove.Where(x => x.Value.CacheKey.Equals(cacheKey, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in filesToRemove)
        {
            this.cached.Remove(file.Key, out _);
            this.cachedById.Remove(file.Value.Id, out _);
        }
    }

    private async Task<CachedFile> GetFile(string url, string cacheKey, CachedFile.FileCategory category)
    {
        var response = await this.client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync();

        var id = Hash.GetSha256Hash(content);

        var cachedPath = new FileInfo(Path.Combine(this.cacheDirectory.FullName, id));
        if (!cachedPath.Exists)
        {
            await File.WriteAllBytesAsync(cachedPath.FullName, content);
        }

        var fileName = response.Content.Headers.ContentDisposition?.FileName;
        fileName ??= url.Split('/').Last();

        return new CachedFile(id, cacheKey, cachedPath, content.Length, response.Content.Headers.ContentType?.MediaType, fileName, category);
    }

    public struct CachedFile
    {
        private readonly WeakReference<byte[]> data = new(null);

        public CachedFile(string id, string cacheKey, FileInfo cachedFile, long length, string? contentType, string originalName, FileCategory category)
        {
            this.CacheKey = cacheKey;
            this.CachedFileInfo = cachedFile;
            this.ContentType = contentType;
            this.OriginalName = originalName;
            this.Id = id;
            this.Category = category;
            this.Length = length;
        }

        public string CacheKey { get; set; }

        public string OriginalName { get; set; }

        public string? ContentType { get; set; }

        public FileInfo CachedFileInfo { get; set; }

        public string Id { get; set; }

        public FileCategory Category { get; set; }

        public long Length { get; private set; }

        public enum FileCategory
        {
            Release,
            Plugin,
            Asset,
            Dalamud,
        }

        public byte[] GetData()
        {
            if (this.data.TryGetTarget(out var cachedData))
            {
                return cachedData;
            }

            var readData = File.ReadAllBytes(CachedFileInfo.FullName);
            this.data.SetTarget(readData);
            return readData;
        }
    }
}