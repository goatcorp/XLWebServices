using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Octokit;
using XLWebServices.Services;
using XLWebServices.Services.PluginData;

namespace XLWebServices.Controllers;

[ApiController]
[Route("Dalamud/_Private/PacRepo/[action]")]
public class PacRepo : ControllerBase
{
    private readonly GitHubService github;
    private readonly IConfiguration config;

    private const string Key = "nuts";

    private static readonly List<(DateTime When, string Nonce)> Nonces = new();

    public PacRepo(GitHubService github, IConfiguration config)
    {
        this.github = github;
        this.config = config;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetRepo([FromQuery] string key)
    {
        if (key != Key)
            return Unauthorized("poop");
        
        var repoOwner = this.config["GitHub:PluginRepositoryD17:Owner"];
        var repoName = this.config["GitHub:PluginRepositoryD17:Name"];

        var prs = await this.github.Client.Repository.PullRequest.GetAllForRepository(repoOwner, repoName, new PullRequestRequest()
        {
            State = ItemStateFilter.Open,
            SortDirection = SortDirection.Descending,
        });
        if (prs == null)
            return StatusCode(500, "prs == null");

        var myNonce = Guid.NewGuid().ToString();
        Nonces.Add((DateTime.UtcNow, myNonce));

        List<PluginManifest> manifests = new();
        foreach (var pr in prs)
        {
            var mf = new PluginManifest();
            mf.IconUrl = "https://goatcorp.github.io/icons/plo_popart_sm.png";
            mf.Name = $"{pr.Title} (#{pr.Number})";
            mf.RepoUrl = pr.HtmlUrl;
            mf.Punchline = mf.Description = $"PR from {pr.User.Login}";
            mf.AssemblyVersion = mf.TestingAssemblyVersion = Version.Parse("1.0.0.0");
            mf.Author = pr.User.Login;
            mf.InternalName = $"PacTestingPlugin{pr.Number}";
            mf.AcceptsFeedback = false;
            mf.Tags = new List<string>()
            {
                "pactesting"
            };
            mf.DownloadLinkInstall = mf.DownloadLinkTesting =
                mf.DownloadLinkUpdate =
                    $"{this.config["HostedUrl"]}/Dalamud/_Private/PacRepo/DownloadPlugin/{pr.Number}?nonce={myNonce}";
            
            manifests.Add(mf);
        }

        return new JsonResult(manifests);
    }
    
    private class Artifact
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("node_id")]
        public string NodeId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("size_in_bytes")]
        public int SizeInBytes { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("archive_download_url")]
        public string ArchiveDownloadUrl { get; set; }

        [JsonPropertyName("expired")]
        public bool Expired { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("workflow_run")]
        public WorkflowRun WorkflowRun { get; set; }
    }

    private class ArtifactsList
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("artifacts")]
        public List<Artifact> Artifacts { get; set; }
    }

    private class WorkflowRun
    {
        /*
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("repository_id")]
        public int RepositoryId { get; set; }

        [JsonPropertyName("head_repository_id")]
        public int HeadRepositoryId { get; set; }

        [JsonPropertyName("head_branch")]
        public string HeadBranch { get; set; }

        [JsonPropertyName("head_sha")]
        public string HeadSha { get; set; }
        */
    }

    #region CheckRunsApi

    // Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);
    private class App
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("node_id")]
        public string NodeId { get; set; }

        [JsonPropertyName("owner")]
        public Owner Owner { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("external_url")]
        public string ExternalUrl { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("permissions")]
        public Permissions Permissions { get; set; }

        [JsonPropertyName("events")]
        public List<object> Events { get; set; }
    }

    private class CheckRun
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("node_id")]
        public string NodeId { get; set; }

        [JsonPropertyName("head_sha")]
        public string HeadSha { get; set; }

        [JsonPropertyName("external_id")]
        public string ExternalId { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        [JsonPropertyName("details_url")]
        public string DetailsUrl { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("conclusion")]
        public string Conclusion { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime CompletedAt { get; set; }

        [JsonPropertyName("output")]
        public Output Output { get; set; }

        [JsonPropertyName("check_suite")]
        public CheckSuite CheckSuite { get; set; }

        [JsonPropertyName("app")]
        public App App { get; set; }

        [JsonPropertyName("pull_requests")]
        public List<object> PullRequests { get; set; }
    }

    private class CheckSuite
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }

    private class Output
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        [JsonPropertyName("text")]
        public object Text { get; set; }

        [JsonPropertyName("annotations_count")]
        public int AnnotationsCount { get; set; }

        [JsonPropertyName("annotations_url")]
        public string AnnotationsUrl { get; set; }
    }

    private class Owner
    {
        [JsonPropertyName("login")]
        public string Login { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("node_id")]
        public string NodeId { get; set; }

        [JsonPropertyName("avatar_url")]
        public string AvatarUrl { get; set; }

        [JsonPropertyName("gravatar_id")]
        public string GravatarId { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        [JsonPropertyName("followers_url")]
        public string FollowersUrl { get; set; }

        [JsonPropertyName("following_url")]
        public string FollowingUrl { get; set; }

        [JsonPropertyName("gists_url")]
        public string GistsUrl { get; set; }

        [JsonPropertyName("starred_url")]
        public string StarredUrl { get; set; }

        [JsonPropertyName("subscriptions_url")]
        public string SubscriptionsUrl { get; set; }

        [JsonPropertyName("organizations_url")]
        public string OrganizationsUrl { get; set; }

        [JsonPropertyName("repos_url")]
        public string ReposUrl { get; set; }

        [JsonPropertyName("events_url")]
        public string EventsUrl { get; set; }

        [JsonPropertyName("received_events_url")]
        public string ReceivedEventsUrl { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("site_admin")]
        public bool SiteAdmin { get; set; }
    }

    private class Permissions
    {
        [JsonPropertyName("actions")]
        public string Actions { get; set; }

        [JsonPropertyName("checks")]
        public string Checks { get; set; }

        [JsonPropertyName("issues")]
        public string Issues { get; set; }

        [JsonPropertyName("metadata")]
        public string Metadata { get; set; }
    }

    private class CheckRunsList
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("check_runs")]
        public List<CheckRun> CheckRuns { get; set; }
    }

    #endregion

    [HttpGet("{prNumber}")]
    public async Task<IActionResult> DownloadPlugin(string prNumber, [FromQuery] string nonce)
    {
#if !DEBUG
        if (!Nonces.Any(x => x.Nonce == nonce && (DateTime.UtcNow - x.When).Hours < 2))
            return Unauthorized();
#endif
        
        var repoOwner = this.config["GitHub:PluginRepositoryD17:Owner"];
        var repoName = this.config["GitHub:PluginRepositoryD17:Name"];

        var pr = await this.github.Client.PullRequest.Get(repoOwner, repoName, int.Parse(prNumber));
        if (pr == null)
            return StatusCode(500, "pr == null");

        var suites = await this.github.Client.Check.Suite.GetAllForReference(repoOwner, repoName, pr.Head.Sha);
        if (suites == null)
            return StatusCode(500, "suites == null");

        var plogonSuite = suites.CheckSuites.FirstOrDefault(x => x.App.Name == "Plogon Build");
        if (plogonSuite == null)
            return StatusCode(500, "plogonSuite == null");
        
        if (plogonSuite.Conclusion != CheckConclusion.Success)
            return StatusCode(500, $"Suite status was {plogonSuite.Conclusion}");
        
        using var client = new HttpClient()
        {
            BaseAddress = new Uri("https://api.github.com/"),
            DefaultRequestHeaders =
            {
                Accept =  { new MediaTypeWithQualityHeaderValue("application/vnd.github+json")},
                UserAgent = { new ProductInfoHeaderValue("XLWebServices", "1.0") },
                Authorization = new AuthenticationHeaderValue("Bearer", this.config["Auth:GitHubToken"]),
            }
        };

        var checkRuns =
            await client.GetFromJsonAsync<CheckRunsList>(
                $"/repos/{repoOwner}/{repoName}/check-suites/{plogonSuite.Id}/check-runs");
        if (checkRuns == null)
            return StatusCode(500, "checkRuns == null");
        if (checkRuns.CheckRuns.Count == 0)
            return StatusCode(500, "No check runs");

        var plogonRun = checkRuns.CheckRuns.First();

        var runId = plogonRun.DetailsUrl
            .Substring(plogonRun.DetailsUrl.LastIndexOf("/", StringComparison.InvariantCulture) + 1);

        var artifactsUrl = $"/repos/{repoOwner}/{repoName}/actions/runs/{runId}/artifacts";
        var artifactsList =
            await client.GetFromJsonAsync<ArtifactsList>(artifactsUrl);
        if (artifactsList == null)
            return StatusCode(500, "artifactsList == null");
        
        if (artifactsList.Artifacts.Count == 0)
            return StatusCode(500, "No artifacts");

        var artifact = artifactsList.Artifacts.First();

        //var artifactReq = new HttpRequestMessage(HttpMethod.Get, );
        //var artifactResp = await client.SendAsync(artifactReq);
        //artifactResp.EnsureSuccessStatusCode();
        
        var artifactZipBytes = await client.GetByteArrayAsync(artifact.ArchiveDownloadUrl);
        using var strm = new MemoryStream(artifactZipBytes);
        using var zip = new ZipArchive(strm, ZipArchiveMode.Read);
        var latestZipEntry = zip.Entries.FirstOrDefault(x => x.FullName.EndsWith("latest.zip"));
        if (latestZipEntry == null)
            return StatusCode(500, "artifactsList == null");

        using var latestZipMemStrm = new MemoryStream();
        using var latestZipZipStrm = latestZipEntry.Open();
        await latestZipZipStrm.CopyToAsync(latestZipMemStrm);
        latestZipMemStrm.Position = 0;
        
        return File(latestZipMemStrm.ToArray(), "application/zip", "latest.zip");
    }
}