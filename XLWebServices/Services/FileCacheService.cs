using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace XLWebServices.Services;

public class FileCacheService
{
    private readonly ConcurrentDictionary<string, CachedFile> cached = new();
    private readonly HttpClient client;

    public long CacheSize => this.cached.Sum(x => x.Value.Data.Length);

    public Dictionary<CachedFile.FileCategory, int> CountPerCategory => this.cached.GroupBy(x => x.Value.Category).ToDictionary(x => x.Key, x => x.Count());

    public FileCacheService()
    {
        this.client = new HttpClient();
        this.client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        };
    }

    public async Task<CachedFile> CacheFile(string fileName, string cacheKey, string url, CachedFile.FileCategory category)
    {
        var key = $"{fileName}-{cacheKey}";

        if (this.cached.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var file = await GetFile(url, cacheKey, category);
        this.cached.TryAdd(key, file);
        return file;
    }

    public CachedFile? GetCachedFile(Guid id)
    {
        return this.cached.Values.FirstOrDefault(x => x.FileId == id);
    }

    public void ClearCategory(CachedFile.FileCategory category, string? cacheKey = null)
    {
        var filesToRemove = this.cached.Where(x => x.Value.Category == category);
        if (cacheKey != null)
        {
            filesToRemove = filesToRemove.Where(x => x.Value.CacheKey.Equals(cacheKey, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in filesToRemove.Select(x => x.Key))
        {
            this.cached.Remove(file, out _);
        }
    }

    private async Task<CachedFile> GetFile(string url, string cacheKey, CachedFile.FileCategory category)
    {
        var response = await this.client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync();

        var fileName = response.Content.Headers.ContentDisposition?.FileName;
        fileName ??= url.Split('/').Last();

        return new CachedFile(cacheKey, content, response.Content.Headers.ContentType?.MediaType, fileName, category);
    }

    public struct CachedFile
    {
        public CachedFile(string cacheKey, byte[] data, string? contentType, string originalName, FileCategory category)
        {
            this.CacheKey = cacheKey;
            this.Data = data;
            this.ContentType = contentType;
            this.OriginalName = originalName;
            this.FileId = Guid.NewGuid();
            this.Category = category;
        }

        public string CacheKey { get; set; }

        public string OriginalName { get; set; }

        public string? ContentType { get; set; }

        public byte[] Data { get; set; }

        public Guid FileId { get; set; }

        public FileCategory Category { get; set; }

        public enum FileCategory
        {
            Release,
            Plugin,
            Asset,
            Dalamud,
        }
    }
}