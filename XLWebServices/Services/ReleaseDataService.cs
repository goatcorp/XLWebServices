using Octokit;

namespace XLWebServices.Services;

public class ReleaseDataService
{
    private readonly ILogger<ReleaseDataService> _logger;
    private readonly GitHubService _github;
    private readonly IConfiguration _configuration;
    private readonly FileCacheService _cache;
    private readonly DiscordHookService _discord;

    public string CachedReleasesList { get; private set; }
    public string CachedPrereleasesList { get; private set; }

    public Release CachedRelease { get; private set; }
    public Release CachedPrerelease { get; private set; }

    public string ReleaseChangelog { get; set; }
    public string PrereleaseChangelog { get; set; }

    public ReleaseDataService(ILogger<ReleaseDataService> logger, GitHubService github, IConfiguration configuration, FileCacheService cache, DiscordHookService discord)
    {
        _logger = logger;
        _github = github;
        _configuration = configuration;
        _cache = cache;
        _discord = discord;
    }

    public async Task ClearCache()
    {
        _logger.LogInformation("Now getting GitHub releases");

        using var client = new HttpClient();

        var repoOwner = _configuration["GitHub:LauncherRepository:Owner"];
        var repoName = _configuration["GitHub:LauncherRepository:Name"];

        try
        {
            var releases = await _github.Client.Repository.Release.GetAll(repoOwner, repoName);

            if (releases == null)
                throw new Exception("Could not get GitHub releases.");

            var ordered = releases.OrderByDescending(x => x.PublishedAt).ToArray();

            Release newPrerelease, newRelease;
            string newPrereleaseFile, newReleaseFile;
            if (ordered.First().Prerelease)
            {
                newPrerelease = ordered.First();
                newRelease = ordered.First(x => !x.Prerelease);

                newPrereleaseFile = await GetReleasesFileForRelease(client, newPrerelease);
                newReleaseFile = await GetReleasesFileForRelease(client, newRelease);
            }
            else
            {
                newRelease = ordered.First();
                newPrerelease = newRelease;

                newReleaseFile = await GetReleasesFileForRelease(client, ordered.First());
                newPrereleaseFile = newReleaseFile;
            }

            var releaseTagValid = await CheckTagSignature(repoOwner, repoName, newRelease.TagName);
            var prereleaseTagValid = await CheckTagSignature(repoOwner, repoName, newPrerelease.TagName);
            if (!releaseTagValid || !prereleaseTagValid)
            {
                _logger.LogError("Invalid tag signature for release or prerelease");
                await _discord.SendError("Invalid tag signature for release or prerelease!", "Tags not signed");
                return;
            }

            this.CachedRelease = newRelease;
            this.CachedPrerelease = newPrerelease;
            this.CachedReleasesList = newReleaseFile;
            this.CachedPrereleasesList = newPrereleaseFile;

            await PrecacheReleaseFiles(CachedRelease);
            await PrecacheReleaseFiles(CachedPrerelease);

            ReleaseChangelog = await client.GetStringAsync(GetDownloadUrlForRelease(CachedRelease, "CHANGELOG.txt"));
            PrereleaseChangelog = await client.GetStringAsync(GetDownloadUrlForRelease(CachedPrerelease, "CHANGELOG.txt"));

            await this._discord.SendSuccess($"Release: {CachedRelease.TagName}({CachedRelease.TargetCommitish})\nPrerelease: {CachedPrerelease.TagName}({CachedPrerelease.TargetCommitish})", "XIVLauncher releases updated!");

            _logger.LogInformation("Correctly refreshed releases");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh releases");
            await _discord.SendError("Could not refresh releases!", "XIVLaunche releases");
            throw;
        }
    }

    private async Task<bool> CheckTagSignature(string repoOwner, string repoName, string tagName)
    {
        var gitTag = await this._github.Client.Git.Tag.Get(repoOwner, repoName, tagName);

        if (gitTag == null)
        {
            _logger.LogError("Couldn't find tag for sig verification: {TagName}", tagName);
            return false;
        }

        if (!gitTag.Verification.Verified)
        {
            _logger.LogError("Tag was not verified: {TagName}", tagName);
            return false;
        }

        if (gitTag.Verification.Signature != this._configuration["TagSig"])
        {
            _logger.LogError("Tag was not signed by the correct signer: {TagName}, {Sig}", tagName, gitTag.Verification.Signature);
            return false;
        }

        return true;
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