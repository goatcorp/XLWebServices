using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class FileController : ControllerBase
{
    private readonly FileCacheService _cache;

    public FileController(FileCacheService cache)
    {
        _cache = cache;
    }

    [HttpGet("{id}")]
    [ResponseCache(Duration = 2592000)]
    public IActionResult Get(Guid id)
    {
         var file = _cache.GetCachedFile(id);

        if (!file.HasValue)
        {
            return NotFound();
        }

        var contentType = file.Value.ContentType;
        contentType ??= "application/octet-stream";

        return File(file.Value.Data, contentType, file.Value.OriginalName);
    }

    [HttpGet]
    public FileMeta Meta()
    {
        return new FileMeta
        {
            CacheSize = this._cache.CacheSize,
            CacheCount = this._cache.CountPerCategory,
        };
    }

    public class FileMeta
    {
        public long CacheSize { get; set; }
        public Dictionary<FileCacheService.CachedFile.FileCategory, int> CacheCount { get; set; }
    }
}