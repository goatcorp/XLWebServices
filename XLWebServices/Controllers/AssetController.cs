using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Asset/[action]")]
public class AssetController : ControllerBase
{
    private readonly AssetCacheService assetCache;
    private readonly IConfiguration configuration;

    public AssetController(AssetCacheService assetCache, IConfiguration configuration)
    {
        this.assetCache = assetCache;
        this.configuration = configuration;
    }

    [HttpGet]
    public IActionResult Meta()
    {
        if (this.assetCache.Response == null)
            return StatusCode(424);

        return new JsonResult(this.assetCache.Response);
    }

    [HttpGet]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        await this.assetCache.ClearCache();

        return Ok();
    }
}