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
                       $"<br><br>Version: {GetGitHash()}", "text/html");
    }

    private static string? _gitHashInternal;

    /// <summary>
    /// Gets the git hash value from the assembly
    /// or null if it cannot be found.
    /// </summary>
    /// <returns>The git hash of the assembly.</returns>
    public static string GetGitHash()
    {
        if (_gitHashInternal != null)
            return _gitHashInternal;

        var asm = typeof(HomeController).Assembly;
        var attrs = asm.GetCustomAttributes<AssemblyMetadataAttribute>();

        _gitHashInternal = attrs.First(a => a.Key == "GitHash").Value!;

        return _gitHashInternal;
    }
}