using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class FileController : ControllerBase
{
    private readonly FileCacheService cache;
    private readonly IConfiguration config;
    
    private static bool alwaysUseFileProxy = false;
    private static bool noAllowForceProxy = false;
    
    private static readonly Counter Requests = Metrics.CreateCounter("file_requests", "File Cache Requests", "Proxy");

    public FileController(FileCacheService cache, IConfiguration config)
    {
        this.cache = cache;
        this.config = config;
    }

    [HttpGet("{id}")]
    [ResponseCache(Duration = 2592000)]
    public IActionResult Get(string id, [FromQuery] bool forceProxy = false)
    {
        var file = this.cache.GetCachedFile(id);

        if (file == null)
        {
            return NotFound();
        }

        if (alwaysUseFileProxy)
        {
            var contentType = file.ContentType;
            contentType ??= "application/octet-stream";
            
            Requests.WithLabels(true.ToString()).Inc();

            return File(file.GetData(), contentType, file.OriginalName);
        }
        
        Requests.WithLabels(false.ToString()).Inc();

        return Redirect(file.OriginalUrl);
    }
    
    [HttpGet("{id}")]
    [ResponseCache(Duration = 2592000)]
    public IActionResult GetProxy(string id)
    {
        var file = this.cache.GetCachedFile(id);

        if (file == null)
        {
            return NotFound();
        }

        if (noAllowForceProxy)
        {
            return Redirect(file.OriginalUrl);
        }
        
        Requests.WithLabels(true.ToString()).Inc();

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
    public async Task<IActionResult> SetUseProxy([FromQuery] string key, [FromQuery] bool useProxy, [FromQuery] bool allowForce)
    {
        if (key != this.config["CacheClearKey"])
            return BadRequest();

        alwaysUseFileProxy = useProxy;
        noAllowForceProxy = allowForce;
        return this.Ok(alwaysUseFileProxy);
    }
    
    public class FileMeta
    {
        public long CacheSize { get; set; }
        public Dictionary<FileCacheService.CachedFile.FileCategory, int> CacheCount { get; set; }
    }
}