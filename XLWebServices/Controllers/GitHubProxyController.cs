using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Octokit;
using Prometheus;
using XLWebServices.Services;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Proxy/[action]")]
public class GitHubProxyController: ControllerBase
{
    private readonly ILogger<GitHubProxyController> _logger;
    private readonly IConfiguration _configuration;
    private readonly RedisService _redis;
    private readonly ReleaseDataService _releaseData;
    private readonly FileCacheService _cache;

    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_startups", "XIVLauncher Unique Startups", "Version");
    private static readonly Counter InstallsOverTime = Metrics.CreateCounter("xl_installs", "XIVLauncher Installs");

    private static readonly Regex SemverRegex = new(@"^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$", RegexOptions.Compiled);

    const string RedisKeyUniqueInstalls = "XLUniqueInstalls";
    const string RedisKeyStarts = "XLStarts";

    public GitHubProxyController(ILogger<GitHubProxyController> logger, IConfiguration configuration, RedisService redis, ReleaseDataService releaseData, FileCacheService cache)
    {
        _logger = logger;
        _configuration = configuration;
        _redis = redis;
        _releaseData = releaseData;
        _cache = cache;
    }

    [HttpGet("{track:alpha}/{file}")]
    public async Task<IActionResult> Update(string file, string track, string? localVersion = null)
    {
        if (!string.IsNullOrEmpty(localVersion))
        {
            if (!SemverRegex.IsMatch(localVersion))
            {
                _logger.LogError("Invalid local version: {LocalVersion}", localVersion);
                return BadRequest("Invalid local version");
            }

            if (file == "RELEASES")
            {
                DownloadsOverTime.WithLabels(localVersion).Inc();
                await _redis.IncrementCount(RedisKeyStarts);
            }
        }
        else if (file == "RELEASES")
        {
            InstallsOverTime.Inc();
            DownloadsOverTime.WithLabels("Setup").Inc();
            await _redis.IncrementCount(RedisKeyUniqueInstalls);
        }

        if (file == "RELEASES")
        {
            switch (track)
            {
                case "Release":
                    return Content(_releaseData.CachedReleasesList);
                case "Prerelease":
                    return Content(_releaseData.CachedPrereleasesList);
            }
        }
        else
        {
            var allowedFileNames = new[] {
                "Setup.exe",
                $"XIVLauncher-{_releaseData.CachedRelease.TagName}-delta.nupkg",
                $"XIVLauncher-{_releaseData.CachedRelease.TagName}-full.nupkg",
                $"XIVLauncher-{_releaseData.CachedPrerelease.TagName}-delta.nupkg",
                $"XIVLauncher-{_releaseData.CachedPrerelease.TagName}-full.nupkg",
            };

            if (!allowedFileNames.Contains(file))
                return this.BadRequest("Not valid filename");

            /*
            switch (track)
            {
                case "Release":
                {
                    var url = ReleaseDataService.GetDownloadUrlForRelease(_releaseData.CachedRelease, file);
                    var cachedFile = await _cache.CacheFile(file,  _releaseData.CachedRelease.TagName, url, FileCacheService.CachedFile.FileCategory.Release);
                    return Redirect($"{this._configuration["HostedUrl"]}/File/Get/{cachedFile.FileId}");
                }

                case "Prerelease":
                {
                    var url = ReleaseDataService.GetDownloadUrlForRelease(_releaseData.CachedPrerelease, file);
                    var cachedFile = await _cache.CacheFile(file,  _releaseData.CachedPrerelease.TagName, url, FileCacheService.CachedFile.FileCategory.Release);
                    return Redirect($"{this._configuration["HostedUrl"]}/File/Get/{cachedFile.FileId}");
                }
            }
            */

            switch (track)
            {
                case "Release":
                    return Redirect(ReleaseDataService.GetDownloadUrlForRelease(_releaseData.CachedRelease, file));
                case "Prerelease":
                    return Redirect(ReleaseDataService.GetDownloadUrlForRelease(_releaseData.CachedPrerelease, file));
            }
        }

        _logger.LogError("Invalid track: {Track}", track);
        return BadRequest("Invalid track");
    }

    [HttpGet]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != _configuration["CacheClearKey"])
            return BadRequest();

        await _releaseData.ClearCache();

        return Ok();
    }

    [HttpGet]
    public async Task<ProxyMeta> Meta()
    {
        return new ProxyMeta
        {
            TotalDownloads = await _redis.GetCount(RedisKeyStarts),
            UniqueInstalls = await _redis.GetCount(RedisKeyUniqueInstalls),
            ReleaseVersion = ProxyMeta.VersionMeta.From(_releaseData.CachedRelease),
            PrereleaseVersion = ProxyMeta.VersionMeta.From(_releaseData.CachedPrerelease),
        };
    }

    public class ProxyMeta
    {
        public long TotalDownloads { get; init; }
        public long UniqueInstalls { get; init; }
        public VersionMeta? ReleaseVersion { get; init; }
        public VersionMeta? PrereleaseVersion { get; init; }

        public class VersionMeta
        {
            public string ReleasesInfo { get; init; }
            public string Version { get; init; }
            public string Url { get; init; }
            public string Changelog { get; init; }
            public DateTime? When { get; init; }

            public static VersionMeta? From(Release? release)
            {
                if (release == null)
                    return null;

                return new VersionMeta
                {
                    ReleasesInfo = $"/Proxy/Update/{(release.Prerelease ? "Prerelease" : "Release")}/RELEASES",
                    Version = release.TagName,
                    Url = release.HtmlUrl,
                    Changelog = release.Body,
                    When = release.PublishedAt?.DateTime,
                };
            }
        }
    }
}