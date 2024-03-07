using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Launcher/[action]")]
public class LauncherController : ControllerBase
{
    private readonly ILogger<GitHubProxyController> _logger;
    private readonly IConfiguration _configuration;
    private readonly FallibleService<RedisService> _redis;
    private readonly FallibleService<LauncherReleaseDataService> _launcherReleaseData;
    private readonly FileCacheService _cache;
    
    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_startups", "XIVLauncher Unique Startups", "Version");
    private static readonly Counter InstallsOverTime = Metrics.CreateCounter("xl_installs", "XIVLauncher Installs");

    private static readonly Regex SemverRegex = new(@"^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$", RegexOptions.Compiled);

    private static bool _isCanaryEnabled = false;
    
    const string RedisKeyUniqueInstalls = "XLUniqueInstalls";
    const string RedisKeyStarts = "XLStarts";

    private static string[] CanaryAirports = new[]
    {
        "lax",
        "cdg",
        "sin",
    };

    private const string TRACK_RELEASE = "Release";
    private const string TRACK_PRERELEASE = "Prerelease";
    
    public LauncherController(ILogger<GitHubProxyController> logger, IConfiguration configuration,
        FallibleService<RedisService> redis, FallibleService<LauncherReleaseDataService> launcherReleaseData,
        FileCacheService cache)
    {
        _logger = logger;
        _configuration = configuration;
        _redis = redis;
        _launcherReleaseData = launcherReleaseData;
        _cache = cache;

        if (string.IsNullOrWhiteSpace(_configuration["LauncherClientConfig:FrontierUrl"]))
            throw new Exception("No frontier URL configured!");
        
        if (string.IsNullOrWhiteSpace(_configuration["LauncherClientConfig:FrontierOrigin"]))
            throw new Exception("No frontier origin configured!");
    }

    [Flags]
    public enum LeaseFeatureFlags
    {
        None = 0,
        GlobalDisableDalamud = 1,
        GlobalDisableLogin = 1 << 1,
    }

    public class Lease
    {
        public bool Success { get; set; }

        public string? Message { get; set; }

        public string FrontierUrl { get; set; }
        
        public string? CutOffBootver { get; set; }

        //public string FrontierOrigin { get; set; }

        public LeaseFeatureFlags Flags { get; set; }

        public string ReleasesList { get; set; }

        public DateTime? ValidUntil { get; set; }
    }

    private string? GetTrack()
    {
        var track = Request.Headers["X-XL-Track"].FirstOrDefault();

        if (track == null || (track != TRACK_RELEASE && track != TRACK_PRERELEASE))
            return null;
        
        if (_isCanaryEnabled && track == TRACK_RELEASE)
        {
            if (Request.Headers.TryGetValue("cf-ray", out var ray))
            {
                var rayText = ray.FirstOrDefault();
                if (rayText != null &&
                    CanaryAirports.Any(x => rayText.ToLower().EndsWith(x)))
                {
                    track = TRACK_PRERELEASE;
                    Response.Headers["X-XL-Canary"] = "yes";
                }
            }
        }

        return track;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetLease()
    {
        var lease = new Lease();
        
        var version = Request.Headers["X-XL-LV"].FirstOrDefault();
        if (version != "0")
            return Content("");

        if (_launcherReleaseData.HasFailed && this._launcherReleaseData.Get()?.CachedReleasesList == null)
        {
            lease.Success = false;
            lease.Message = "Precondition failed";
            Response.StatusCode = StatusCodes.Status500InternalServerError; // fallback to "failed to check for updates"
            return new JsonResult(lease);
        }

        var track = GetTrack();
        if (string.IsNullOrEmpty(track))
        {
            lease.Success = false;
            lease.Message = "Unknown or invalid track.";
            Response.StatusCode = StatusCodes.Status400BadRequest; // fallback to "failed to check for updates"
            return new JsonResult(lease);
        }
        
        var haveVersion = Request.Headers["X-XL-ClientHaveVersion"].FirstOrDefault();
        if (!string.IsNullOrEmpty(haveVersion))
        {
            if (!SemverRegex.IsMatch(haveVersion))
            {
                _logger.LogError("Invalid local version: {LocalVersion}", haveVersion);
                return BadRequest("Invalid local version");
            }

            DownloadsOverTime.WithLabels(haveVersion).Inc();
                
            if (!_redis.HasFailed)
                await _redis.Get()!.IncrementCount(RedisKeyStarts);
        }
        else
        {
            DownloadsOverTime.WithLabels("Unknown").Inc();
        }
        
        var isFirstStartup = Request.Headers["X-XL-FirstStart"].FirstOrDefault() == "yes";
        if (isFirstStartup)
        {
            InstallsOverTime.Inc();
            DownloadsOverTime.WithLabels("Setup").Inc();
            
            if (!_redis.HasFailed)
                await _redis.Get()!.IncrementCount(RedisKeyUniqueInstalls);
        }
        
        lease.FrontierUrl = _configuration["LauncherClientConfig:FrontierUrl"] ?? throw new Exception("No frontier URL in config!");
        lease.CutOffBootver = _configuration["LauncherClientConfig:CutOffBootVer"];
        //lease.FrontierOrigin = _configuration["LauncherClientConfig:FrontierOrigin"];
        lease.Flags = LeaseFeatureFlags.None;

        if (_configuration["LauncherClientConfig:GlobalDisableDalamud"]?.ToLower() == "true")
            lease.Flags |= LeaseFeatureFlags.GlobalDisableDalamud;
        
        if (_configuration["LauncherClientConfig:GlobalDisableLogin"]?.ToLower() == "true")
            lease.Flags |= LeaseFeatureFlags.GlobalDisableLogin;
        
        switch (track)
        {
            case TRACK_RELEASE:
                lease.ReleasesList = _launcherReleaseData.Get()!.CachedReleasesList!;
                break;
            case TRACK_PRERELEASE:
                lease.ReleasesList = _launcherReleaseData.Get()!.CachedPrereleasesList!;
                break;
            default:
                throw new ArgumentException($"Unknown track: {track}");
        }
        
        lease.ValidUntil = DateTime.UtcNow + TimeSpan.FromDays(2);
        lease.Success = true;

        return new JsonResult(lease);
    }

    [HttpGet("{file}")]
    public async Task<IActionResult> GetFile(string file)
    {
        var allowedFileNames = new[] {
            "Setup.exe",
            $"XIVLauncher-{this._launcherReleaseData.Get()!.CachedRelease!.TagName}-delta.nupkg",
            $"XIVLauncher-{this._launcherReleaseData.Get()!.CachedRelease!.TagName}-full.nupkg",
            $"XIVLauncher-{this._launcherReleaseData.Get()!.CachedPrerelease!.TagName}-delta.nupkg",
            $"XIVLauncher-{this._launcherReleaseData.Get()!.CachedPrerelease!.TagName}-full.nupkg",
            "CHANGELOG.txt",
        };

        if (!allowedFileNames.Contains(file))
            return this.BadRequest("Not valid filename");
        
        var track = GetTrack();
        if (string.IsNullOrEmpty(track))
            return BadRequest("Bad track");
        
        switch (track)
        {
            case TRACK_RELEASE:
            {
                var url = LauncherReleaseDataService.GetDownloadUrlForRelease(_launcherReleaseData.Get()!.CachedRelease!, file);
                var cachedFile = await _cache.CacheFile(file,  _launcherReleaseData.Get()!.CachedRelease!.TagName, url, FileCacheService.CachedFile.FileCategory.Release);
                return Redirect($"{_configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
            }
            case TRACK_PRERELEASE:
            {
                var url = LauncherReleaseDataService.GetDownloadUrlForRelease(_launcherReleaseData.Get()!.CachedPrerelease!, file);
                var cachedFile = await _cache.CacheFile(file,  _launcherReleaseData.Get()!.CachedPrerelease!.TagName, url, FileCacheService.CachedFile.FileCategory.Release);
                return Redirect($"{_configuration["HostedUrl"]}/File/Get/{cachedFile.Id}");
            }
            default:
                throw new ArgumentException($"Unknown track: {track}");
        }
    }
}