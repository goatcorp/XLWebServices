using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class FileController : ControllerBase
{
    private readonly FileCacheService cache;
    private readonly IConfiguration config;
    
    private static bool useFileProxy = true;

    public FileController(FileCacheService cache, IConfiguration config)
    {
        this.cache = cache;
        this.config = config;
    }

    [HttpGet("{id}")]
    [ResponseCache(Duration = 2592000)]
    public IActionResult Get(string id)
    {
        var file = this.cache.GetCachedFile(id);

        if (file == null)
        {
            return NotFound();
        }

        if (!useFileProxy)
            return Redirect(file.OriginalUrl);

        var contentType = file.ContentType;
        contentType ??= "application/octet-stream";

        return File(file.GetData(), contentType, file.OriginalName);
    }

    [HttpGet]
    public FileMeta Meta()
    {
        return new FileMeta
        {
            CacheSize = this.cache.CacheSize,
            CacheCount = this.cache.CountPerCategory,
        };
    }

    [HttpPost]
    public async Task<IActionResult> SetUseProxy([FromQuery] string key, [FromQuery] bool useProxy)
    {
        if (key != this.config["CacheClearKey"])
            return BadRequest();

        useFileProxy = useProxy;
        return this.Ok(useFileProxy);
    }
    
    public class FileMeta
    {
        public long CacheSize { get; set; }
        public Dictionary<FileCacheService.CachedFile.FileCategory, int> CacheCount { get; set; }
    }
}