using Octokit;

namespace XLWebServices.Services;

public class ReleaseDataService
{
    private readonly ILogger<ReleaseDataService> _logger;
    private readonly GitHubService _github;
    private readonly IConfiguration _configuration;
    private readonly FileCacheService _cache;

    public string CachedReleasesList { get; private set; }
    public string CachedPrereleasesList { get; private set; }

    public Release CachedRelease { get; private set; }
    public Release CachedPrerelease { get; private set; }

    public ReleaseDataService(ILogger<ReleaseDataService> logger, GitHubService github, IConfiguration configuration, FileCacheService cache)
    {
        _logger = logger;
        _github = github;
        _configuration = configuration;
        _cache = cache;
    }

    public async Task ClearCache()
    {
        _logger.LogInformation("Now getting GitHub releases");

        using var client = new HttpClient();

        var repoOwner = _configuration["GitHub:LauncherRepository:Owner"];
        var repoName = _configuration["GitHub:LauncherRepository:Name"];

        var previousRelease = CachedRelease;
        var previousPrerelease = CachedPrerelease;

        try
        {
            var releases = await _github.Client.Repository.Release.GetAll(repoOwner, repoName);

            if (releases == null)
                throw new Exception("Could not get GitHub releases.");

            var ordered = releases.OrderByDescending(x => x.PublishedAt);

            if (ordered.First().Prerelease)
            {
                this.CachedPrerelease = ordered.First();
                this.CachedRelease = ordered.First(x => !x.Prerelease);

                this.CachedPrereleasesList = await GetReleasesFileForRelease(client, this.CachedPrerelease);
                this.CachedReleasesList = await GetReleasesFileForRelease(client, this.CachedRelease);
            }
            else
            {
                this.CachedRelease = ordered.First();
                this.CachedPrerelease = this.CachedRelease;

                this.CachedReleasesList = await GetReleasesFileForRelease(client, ordered.First());
                this.CachedPrereleasesList = this.CachedReleasesList;
            }

            await PrecacheReleaseFiles(CachedRelease);
            await PrecacheReleaseFiles(CachedPrerelease);

            _logger.LogInformation("Correctly refreshed releases");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh releases");
            throw;
        }
    }

    private async Task PrecacheReleaseFiles(Release release)
    {
        var fullNupkgName = $"XIVLauncher-{release.TagName}-full.nupkg";
        await _cache.CacheFile(fullNupkgName, release.TagName, GetDownloadUrlForRelease(release, fullNupkgName),
            FileCacheService.CachedFile.FileCategory.Release);

        var deltaNupkgName = $"XIVLauncher-{release.TagName}-delta.nupkg";
        await _cache.CacheFile(deltaNupkgName, release.TagName, GetDownloadUrlForRelease(release, deltaNupkgName),
            FileCacheService.CachedFile.FileCategory.Release);

        var setupExeName = "Setup.exe";
        await _cache.CacheFile(setupExeName, release.TagName, GetDownloadUrlForRelease(release, setupExeName),
            FileCacheService.CachedFile.FileCategory.Release);
    }

    public static string GetDownloadUrlForRelease(Release entry, string fileName) => entry.HtmlUrl.Replace("/tag/", "/download/") + "/" + fileName;

    public static async Task<string> GetReleasesFileForRelease(HttpClient client, Release entry) => await client.GetStringAsync(GetDownloadUrlForRelease(entry, "RELEASES"));
}