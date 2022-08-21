using Microsoft.AspNetCore.Mvc;
using Prometheus;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Dalamud/Metric/[action]")]
public class MetricController : Controller
{
    private static readonly Summary LifetimeGauge =
        Metrics.CreateSummary("xl_dalamud_crash_lifetime", "Lifetime of the application");

    private static readonly Counter CodeCounter =
        Metrics.CreateCounter("xl_dalamud_crashes", "Crashes in total", new[] {"Code"});
    
    [HttpGet]
    public IActionResult ReportCrash([FromQuery] string lt, [FromQuery] string code)
    {
        if (!code.StartsWith("c") || !code.EndsWith("5"))
            return BadRequest();
        
        LifetimeGauge.Observe(double.Parse(lt));
        CodeCounter.WithLabels(code).Inc();
        
        return Ok();
    }
}