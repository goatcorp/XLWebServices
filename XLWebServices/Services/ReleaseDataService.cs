using System.Diagnostics;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Octokit;
using Repository = LibGit2Sharp.Repository;

namespace XLWebServices.Services;

public class ReleaseDataService
{
    private readonly ILogger<ReleaseDataService> _logger;
    private readonly GitHubService _github;
    private readonly IConfiguration _configuration;
    private readonly FileCacheService _cache;
    private readonly DiscordHookService _discord;

    public string? CachedReleasesList { get; private set; }
    public string? CachedPrereleasesList { get; private set; }

    public Release? CachedRelease { get; private set; }
    public Release? CachedPrerelease { get; private set; }

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

        var prevRelease = CachedRelease;
        var prevPrerelease = CachedPrerelease;

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

            ReleaseChangelog = await client.GetStringAsync(GetDownloadUrlForRelease(CachedRelease, "CHANGELOG.txt"));
            PrereleaseChangelog = await client.GetStringAsync(GetDownloadUrlForRelease(CachedPrerelease, "CHANGELOG.txt"));

            this.CachedRelease = newRelease;
            this.CachedPrerelease = newPrerelease;
            this.CachedReleasesList = newReleaseFile;
            this.CachedPrereleasesList = newPrereleaseFile;

            await PrecacheReleaseFiles(CachedRelease);
            await PrecacheReleaseFiles(CachedPrerelease);

            if (prevRelease != null && prevRelease.TagName != CachedRelease.TagName || prevPrerelease != null && prevPrerelease.TagName != CachedPrerelease.TagName)
            {
                await this._discord.SendSuccess($"Release: {CachedRelease.TagName}({CachedRelease.TargetCommitish})\nPrerelease: {CachedPrerelease.TagName}({CachedPrerelease.TargetCommitish})", "XIVLauncher releases updated!");
            }

            _logger.LogInformation("Correctly refreshed releases");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh releases");
            await _discord.SendError("Could not refresh releases!", "XIVLaunche releases");
            throw;
        }
    }

    private static Regex _goodSigRegex =
        new("\\[GNUPG\\:\\] GOODSIG (?<keyid>[A-Z0-9]{16})", RegexOptions.Compiled);

    private async Task<bool> CheckTagSignature(string repoOwner, string repoName, string tagName)
    {
        return true;

        var tag = await _github.Client.Git.Reference.Get(repoOwner, repoName, "tags/" + tagName);
        var tagSha = tag.Object.Sha;
        var gitTag = await this._github.Client.Git.Tag.Get(repoOwner, repoName, tagSha);

        if (gitTag == null)
        {
            _logger.LogError("Couldn't find tag for sig verification: {TagName}", tagSha);
            return false;
        }

        if (!gitTag.Verification.Verified)
        {
            _logger.LogError("Tag was not verified: {TagName}", tagSha);
            return false;
        }

        var tmpFolder = Path.Combine(Path.GetTempPath(), "xl-repo", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpFolder);

        Repository.Init(tmpFolder);
        var repo = new Repository(tmpFolder);
        repo.Network.Remotes.Add("origin", $"https://github.com/{repoOwner}/{repoName}.git");
        repo.Network.Fetch("origin", new [] { "refs/tags/" + tagName }, new FetchOptions { TagFetchMode = TagFetchMode.All });

        var gitProcessInfo = new ProcessStartInfo("git", $"verify-tag --raw {tagName}")
        {
            WorkingDirectory = tmpFolder,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var gitProcess = Process.Start(gitProcessInfo);

        if (gitProcess == null)
        {
            this._logger.LogError("Couldn't start git");
            return false;
        }

        var err = await gitProcess.StandardError.ReadToEndAsync();
        await gitProcess.WaitForExitAsync();

        var keyIdMatch = _goodSigRegex.Match(err).Groups["keyid"];
        if (keyIdMatch.Success)
        {
            if (keyIdMatch.Value != this._configuration["TagSig"])
            {
                _logger.LogError("Tag was not signed by the correct signer: {TagName}, {Sig}", tagSha, keyIdMatch.Value);
                return false;
            }
        }
        else
        {
            _logger.LogError("Tag was not signed correctly");
            return false;
        }

        Thread.Sleep(1000);

        try
        {
            Directory.Delete(tmpFolder, true);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Couldn't delete temp folder");
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