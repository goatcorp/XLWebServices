using Octokit;

namespace XLWebServices.Services;

public class GitHubService
{
    public GitHubClient Client { get; private set; }

    public GitHubService(IConfiguration configuration)
    {
        Client = new GitHubClient(new ProductHeaderValue("XLWebServices"))
        {
            Credentials = new Credentials(configuration["Auth:GitHubToken"])
        };
    }
}