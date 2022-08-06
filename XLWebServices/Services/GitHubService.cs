using Octokit;

namespace XLWebServices.Services;

public class GitHubService
{
    public GitHubClient Client { get; private set; }

    public GitHubService(IConfiguration configuration, ILogger<GitHubService> logger)
    {
        Client = new GitHubClient(new ProductHeaderValue("XLWebServices"))
        {
            Credentials = new Credentials(configuration["Auth:GitHubToken"])
        };

        var limits = Client.Miscellaneous.GetRateLimits().GetAwaiter().GetResult();
        if (limits != null)
        {
            logger.LogInformation("RATE LIMITS: {Remaining}/{Limit}, reset: {Reset}", limits.Rate.Remaining, limits.Rate.Limit, limits.Rate.Reset);
        }
    }
}