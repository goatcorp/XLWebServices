using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Dalamud/Asset/[action]")]
[TypeFilter(typeof(AssetCacheService.AssetCacheAvailabilityFilter), IsReusable = true)]
public class AssetController : ControllerBase
{
    private readonly FallibleService<AssetCacheService> assetCache;
    private readonly IConfiguration configuration;

    public AssetController(FallibleService<AssetCacheService> assetCache, IConfiguration configuration)
    {
        this.assetCache = assetCache;
        this.configuration = configuration;
    }

    [HttpGet]
    public IActionResult Meta()
    {
        if (this.assetCache.HasFailed && this.assetCache.Get()?.Response == null)
            return StatusCode(500, "Precondition failed");

        return new JsonResult(this.assetCache.Get()!.Response);
    }

    [HttpPost]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != this.configuration["CacheClearKey"])
            return BadRequest();

        await this.assetCache.RunFallibleAsync(s => s.ClearCache());

        return Ok(this.assetCache.HasFailed);
    }
}