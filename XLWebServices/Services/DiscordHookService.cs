using Discord;
using Discord.Webhook;

namespace XLWebServices.Services;

public class DiscordHookService
{
    private readonly IConfiguration _configuration;

    public DiscordWebhookClient Client { get; private set; }

    private string FooterText => $"XLWebServices {Util.GetGitHash()}";

    public DiscordHookService(IConfiguration configuration)
    {
        _configuration = configuration;
        this.Client = new DiscordWebhookClient(this._configuration["DiscordWebhook"]);
    }

    public async Task SendSuccess(string message, string title)
    {
        var embed = new EmbedBuilder().WithColor(Color.Green).WithTitle(title).WithFooter(FooterText).WithDescription(message).Build();
        await this.Client.SendMessageAsync(embeds: new[] { embed });
    }

    public async Task SendError(string message, string title)
    {
        var embed = new EmbedBuilder().WithColor(Color.Red).WithTitle(title).WithFooter(FooterText).WithDescription(message).Build();
        await this.Client.SendMessageAsync(embeds: new[] { embed });
    }
}