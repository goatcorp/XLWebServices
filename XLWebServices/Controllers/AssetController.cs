using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Asset/[action]")]
public class AssetController : ControllerBase
{
    private readonly AssetCacheService assetCache;

    public AssetController(AssetCacheService assetCache)
    {
        this.assetCache = assetCache;
    }

    [HttpGet]
    public IActionResult Meta()
    {
        if (this.assetCache.Response == null)
            return StatusCode(424);

        return new JsonResult(this.assetCache.Response);
    }
}