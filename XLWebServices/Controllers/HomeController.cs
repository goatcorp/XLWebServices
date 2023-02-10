using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using XLWebServices.Services.JobQueue;

namespace XLWebServices.Controllers;

[ApiController]
public class HomeController : ControllerBase
{
    private readonly IBackgroundTaskQueue _queue;

    public HomeController(IBackgroundTaskQueue queue)
    {
        _queue = queue;
    }
    
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        return Content("<h1>XL Web Services</h1>" +
                       "This server provides updates for XIVLauncher and the plugin listing for Dalamud.<br>" +
                       "<a href=\"https://goatcorp.github.io/faq/xl_troubleshooting#q-are-xivlauncher-dalamud-and-dalamud-plugins-safe-to-use\">Read more here.</a>" +
                       $"<br><br>Version: {Util.GetGitHash()}" + 
                       $"<br>Jobs in queue: {_queue.NumJobsInQueue}",
            "text/html");
    }
}