using System.Collections.Concurrent;

namespace XLWebServices.Services;

public class FileCacheService
{
    private readonly ConcurrentDictionary<string, CachedFile> _cached = new();
    private readonly HttpClient _client = new();

    public long CacheSize => _cached.Sum(x => x.Value.Data.Length);

    public Dictionary<CachedFile.FileCategory, int> CountPerCategory => _cached.GroupBy(x => x.Value.Category).ToDictionary(x => x.Key, x => x.Count());

    public async Task<CachedFile> CacheFile(string fileName, string cacheKey, string url, CachedFile.FileCategory category)
    {
        var key = $"{fileName}-{cacheKey}";

        if (_cached.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var file = await GetFile(url, category);
        _cached.TryAdd(key, file);
        return file;
    }

    public CachedFile? GetCachedFile(Guid id)
    {
        return _cached.Values.FirstOrDefault(x => x.FileId == id);
    }

    public void ClearCategory(CachedFile.FileCategory category)
    {
        var filesToRemove = _cached.Where(x => x.Value.Category == category).Select(x => x.Key).ToList();
        foreach (var file in filesToRemove)
        {
            _cached.Remove(file, out _);
        }
    }

    private async Task<CachedFile> GetFile(string url, CachedFile.FileCategory category)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync();

        var fileName = response.Content.Headers.ContentDisposition?.FileName;
        fileName ??= url.Split('/').Last();

        return new CachedFile(content, response.Content.Headers.ContentType?.MediaType, fileName, category);
    }

    public struct CachedFile
    {
        public CachedFile(byte[] data, string? contentType, string originalName, FileCategory category)
        {
            this.Data = data;
            this.ContentType = contentType;
            this.OriginalName = originalName;
            this.FileId = Guid.NewGuid();
            this.Category = category;
        }

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
        }
    }
}