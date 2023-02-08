using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("/Plogon/[action]")]
public class PlogonController : ControllerBase
{
    private readonly FallibleService<RedisService> _redis;
    private readonly IConfiguration _config;

    private const string MsgIdsKey = "PLOGONSTREAM_MSGS-";
    private const string ChangelogKey = "PLOGONSTREAM_CHANGELOG-";

    public PlogonController(FallibleService<RedisService> redis, IConfiguration config)
    {
        _redis = redis;
        _config = config;
    }
    
    [HttpPost]
    public async Task<IActionResult> RegisterMessageId(
        [FromQuery] string key,
        [FromQuery] string prNumber,
        [FromQuery] string messageId)
    {
        if (!CheckAuth(key))
            return Unauthorized();

        await _redis.Get()!.Database.ListRightPushAsync(MsgIdsKey + prNumber, messageId);
        
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetMessageIds([FromQuery] string prNumber)
    {
        var ids = await _redis.Get()!.Database.ListRangeAsync(MsgIdsKey + prNumber);
        if (ids == null)
            return NotFound();
        
        var idList = ids.Select(redisValue => redisValue.ToString()).ToList();

        return new JsonResult(idList);
    }

    [HttpPost]
    public async Task<IActionResult> RegisterVersionPrNumber(
        [FromQuery] string key,
        [FromQuery] string internalName,
        [FromQuery] string version,
        [FromQuery] string prNumber)
    {
        if (!CheckAuth(key))
            return Unauthorized();
        
        await _redis.Get()!.Database.StringSetAsync(ChangelogKey + $"{internalName}-{version}", prNumber);
        
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetVersionChangelog([FromQuery] string internalName, [FromQuery] string version)
    {
        var changelog = await _redis.Get()!.Database.StringGetAsync(ChangelogKey + $"{internalName}-{version}");
        if (!changelog.HasValue)
            return NotFound();

        return Content(changelog.ToString());
    }

    private bool CheckAuth(string key)
    {
        return key == _config["PlogonApiKey"];
    }
}