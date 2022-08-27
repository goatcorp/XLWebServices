using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("/Plogon/[action]")]
public class PlogonController : ControllerBase
{
    private readonly RedisService _redis;

    private const string MsgIdsKey = "PLOGONSTREAM_MSGS-";
    private const string ChangelogKey = "PLOGONSTREAM_CHANGELOG-";

    public PlogonController(RedisService redis)
    {
        _redis = redis;
    }
    
    [HttpPost]
    public async Task<IActionResult> RegisterMessageId(
        [FromQuery] string key,
        [FromQuery] string prNumber,
        [FromQuery] string messageId)
    {
        if (!CheckAuth(key))
            return Unauthorized();

        await _redis.Database.ListRightPushAsync(MsgIdsKey + prNumber, messageId);
        
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetMessageIds([FromQuery] string prNumber)
    {
        var ids = await _redis.Database.ListRangeAsync(MsgIdsKey + prNumber);
        if (ids == null)
            return NotFound();
        
        var idList = ids.Select(redisValue => redisValue.ToString()).ToList();

        return new JsonResult(idList);
    }

    private class ChangelogBody
    {
        public string Text { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> RegisterVersionChangelog(
        [FromQuery] string key,
        [FromQuery] string internalName,
        [FromQuery] string version)
    {
        if (!CheckAuth(key))
            return Unauthorized();

        var body = await Request.ReadFromJsonAsync<ChangelogBody>();
        if (body == null || body.Text == null)
            return BadRequest("no body");

        await _redis.Database.StringSetAsync(ChangelogKey + $"{internalName}-{version}", body.Text);
        
        return Ok();
    }

    public async Task<IActionResult> GetVersionChangelog([FromQuery] string internalName, [FromQuery] string version)
    {
        var changelog = await _redis.Database.StringGetAsync(ChangelogKey + $"{internalName}-{version}");
        if (!changelog.HasValue)
            return NotFound();

        return Content(changelog.ToString());
    }

    private bool CheckAuth(string key)
    {
        return key == "pWIFpTsd18vkW4gCJje7h";
    }
}