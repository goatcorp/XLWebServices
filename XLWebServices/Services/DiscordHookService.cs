using Discord;
using Discord.Webhook;

namespace XLWebServices.Services;

public class DiscordHookService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DiscordHookService> _logger;

    private readonly DiscordWebhookClient? _adminClient;
    private readonly DiscordWebhookClient? _releasesClient;
    private readonly DiscordWebhookClient? _releasesTestingClient;

    private string AdminFooterText => $"XLWebServices {Util.GetGitHash()}";

    public DiscordHookService(IConfiguration configuration, ILogger<DiscordHookService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _adminClient = SetupClient(_configuration["DiscordWebHooks:Admin"]);
        _releasesClient = SetupClient(_configuration["DiscordWebHooks:Releases"]);
        _releasesTestingClient = SetupClient(_configuration["DiscordWebHooks:ReleasesTesting"]);
    }

    private static DateTime GetPacificStandardTime()
    {
        var utc = DateTime.UtcNow;
        var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var pacificTime = TimeZoneInfo.ConvertTimeFromUtc(utc, pacificZone);
        return pacificTime;
    }
    
    public async Task SendRelease(Embed embed, bool isTesting)
    {
        var client = isTesting ? _releasesTestingClient : _releasesClient;
        if (client == null)
            return;
        
        var time = GetPacificStandardTime();
        var username = "Plo";
        var avatarUrl = "https://goatcorp.github.io/icons/plo.png";
        if (time.Hour is > 20 or < 7)
        {
            username = "Gon";
            avatarUrl = "https://goatcorp.github.io/icons/gon.png";
        }
        
        await client.SendMessageAsync(embeds: new[] { embed }, username: username, avatarUrl: avatarUrl);
    }
    
    private DiscordWebhookClient? SetupClient(string? url) => string.IsNullOrEmpty(url) ? null : new DiscordWebhookClient(url);

    public async Task AdminSendSuccess(string message, string title)
    {
        if (_adminClient == null)
            return;
        
        var embed = new EmbedBuilder().WithColor(Color.Green).WithTitle(title).WithFooter(AdminFooterText).WithDescription(message).Build();
        await _adminClient.SendMessageAsync(embeds: new[] { embed });
    }

    public async Task AdminSendError(string message, string title)
    {
        if (_adminClient == null)
            return;
        
        var embed = new EmbedBuilder().WithColor(Color.Red).WithTitle(title).WithFooter(AdminFooterText).WithDescription(message).Build();
        await _adminClient.SendMessageAsync(embeds: new[] { embed });
    }
}