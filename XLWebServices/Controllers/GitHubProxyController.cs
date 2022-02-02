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
    private readonly GitHubService _github;

    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_startups", "XIVLauncher Unique Startups", "Version");
    private static readonly Counter InstallsOverTime = Metrics.CreateCounter("xl_installs", "XIVLauncher Installs");

    private static string? _cachedReleasesList;
    private static string? _cachedPrereleasesList;

    private static Release? _cachedRelease;
    private static Release? _cachedPrerelease;

    private static readonly Regex SemverRegex = new(@"^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$", RegexOptions.Compiled);

    private static readonly ManualResetEvent Signal = new(true);

    const string RedisKeyUniqueInstalls = "XLUniqueInstalls";
    const string RedisKeyStarts = "XLStarts";

    public GitHubProxyController(ILogger<GitHubProxyController> logger, IConfiguration configuration, RedisService redis, GitHubService github)
    {
        _logger = logger;
        _configuration = configuration;
        _redis = redis;
        _github = github;
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

        if (_cachedReleasesList == null || _cachedPrereleasesList == null)
        {
            if (!Signal.WaitOne(0))
            {
                _logger.LogInformation("Now waiting on refresh");
                Signal.WaitOne();
            }
            else
            {
                Signal.Reset();
                await SetupReleasesAsync();
                Signal.Set();
            }
        }

        if (file == "RELEASES")
        {
            switch (track)
            {
                case "Release":
                    return Content(_cachedReleasesList);
                case "Prerelease":
                    return Content(_cachedPrereleasesList);
            }
        }
        else
        {
            switch (track)
            {
                case "Release":
                    return Redirect(GetDownloadUrlForRelease(_cachedRelease, file));
                case "Prerelease":
                    return Redirect(GetDownloadUrlForRelease(_cachedPrerelease, file));
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

        await SetupReleasesAsync();

        return Ok();
    }

    [HttpGet]
    public async Task<ProxyMeta> Meta()
    {
        return new ProxyMeta
        {
            TotalDownloads = await _redis.GetCount(RedisKeyStarts),
            UniqueInstalls = await _redis.GetCount(RedisKeyUniqueInstalls),
            ReleaseVersion = ProxyMeta.VersionMeta.From(_cachedRelease),
            PrereleaseVersion = ProxyMeta.VersionMeta.From(_cachedPrerelease),
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
            public string ChangelogUrl { get; init; }
            public DateTime? When { get; init; }

            public static VersionMeta? From(Release? release)
            {
                if (release == null)
                    return null;

                return new VersionMeta
                {
                    ReleasesInfo = $"/Proxy/Update/{(release.Prerelease ? "Prerelease" : "Release")}/RELEASES",
                    Version = release.TagName,
                    ChangelogUrl = release.HtmlUrl,
                    When = release.PublishedAt?.DateTime,
                };
            }
        }
    }

    private async Task SetupReleasesAsync()
    {
        Signal.Reset();

        _cachedPrerelease = _cachedRelease = null;
        _cachedPrereleasesList = _cachedReleasesList = null;

        using var client = new HttpClient();

        var repoOwner = _configuration["GitHub:LauncherRepository:Owner"];
        var repoName = _configuration["GitHub:LauncherRepository:Name"];

        try
        {
            var releases = await this._github.Client.Repository.Release.GetAll(repoOwner, repoName);

            if (releases == null)
                throw new Exception("Could not get GitHub releases.");

            var ordered = releases.OrderByDescending(x => x.PublishedAt);

            if (ordered.First().Prerelease)
            {
                _cachedPrerelease = ordered.First();
                _cachedRelease = ordered.First(x => !x.Prerelease);

                _cachedPrereleasesList = await GetReleasesFileForRelease(client, _cachedPrerelease);
                _cachedReleasesList = await GetReleasesFileForRelease(client, _cachedRelease);
            }
            else
            {
                _cachedRelease = ordered.First();
                _cachedPrerelease = _cachedRelease;

                _cachedReleasesList = await GetReleasesFileForRelease(client, ordered.First());
                _cachedPrereleasesList = _cachedReleasesList;
            }

            _logger.LogInformation("Correctly refreshed releases");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh releases");
            throw;
        }
        finally
        {
            Signal.Set();
        }
    }

    private static string GetDownloadUrlForRelease(Release entry, string fileName) => entry.HtmlUrl.Replace("/tag/", "/download/") + "/" + fileName;

    private static async Task<string> GetReleasesFileForRelease(HttpClient client, Release entry) => await client.GetStringAsync(GetDownloadUrlForRelease(entry, "RELEASES"));
}