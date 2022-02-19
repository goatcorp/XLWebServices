using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace XLWebServices.Controllers;

[ApiController]
public class HomeController : ControllerBase
{
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        return Content("<h1>XL Web Services</h1>" +
                       "This server provides updates for XIVLauncher and the plugin listing for Dalamud.<br>" +
                       "<a href=\"https://goatcorp.github.io/faq/xl_troubleshooting#q-are-xivlauncher-dalamud-and-dalamud-plugins-safe-to-use\">Read more here.</a>" +
                       $"<br><br>Version: {Util.GetGitHash()}", "text/html");
    }
}