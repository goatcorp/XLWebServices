using System.Text.Json.Serialization;

namespace XLWebServices.Services.PluginData;

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class PluginManifest
{
    /// <summary>
    ///     Gets the author/s of the plugin.
    /// </summary>
    [JsonPropertyName("Author")]
    public string? Author { get; set; }

    /// <summary>
    ///     Gets or sets the public name of the plugin.
    /// </summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    /// <summary>
    ///     Gets a punchline of the plugins functions.
    /// </summary>
    [JsonPropertyName("Punchline")]
    public string? Punchline { get; set; }

    /// <summary>
    ///     Gets a description of the plugins functions.
    /// </summary>
    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Gets a changelog.
    /// </summary>
    [JsonPropertyName("Changelog")]
    public string? Changelog { get; set; }

    /// <summary>
    ///     Gets a list of tags defined on the plugin.
    /// </summary>
    [JsonPropertyName("Tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    ///     Gets a list of category tags defined on the plugin.
    /// </summary>
    [JsonPropertyName("CategoryTags")]
    public List<string>? CategoryTags { get; set; }

    /// <summary>
    ///     Gets a value indicating whether or not the plugin is hidden in the plugin installer.
    ///     This value comes from the plugin master and is in addition to the list of hidden names kept by Dalamud.
    /// </summary>
    [JsonPropertyName("IsHide")]
    public bool IsHide { get; set; }

    /// <summary>
    ///     Gets the internal name of the plugin, which should match the assembly name of the plugin.
    /// </summary>
    [JsonPropertyName("InternalName")]
    public string InternalName { get; set; }

    /// <summary>
    ///     Gets the current assembly version of the plugin.
    /// </summary>
    [JsonPropertyName("AssemblyVersion")]
    public Version AssemblyVersion { get; set; }

    /// <summary>
    ///     Gets the current testing assembly version of the plugin.
    /// </summary>
    [JsonPropertyName("TestingAssemblyVersion")]
    public Version? TestingAssemblyVersion { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the <see cref="AssemblyVersion" /> is not null.
    /// </summary>
    [JsonIgnore]
    public bool HasAssemblyVersion => this.AssemblyVersion != null;

    /// <summary>
    ///     Gets a value indicating whether the <see cref="TestingAssemblyVersion" /> is not null.
    /// </summary>
    [JsonIgnore]
    public bool HasTestingAssemblyVersion => this.TestingAssemblyVersion != null;

    /// <summary>
    ///     Gets a value indicating whether the plugin is only available for testing.
    /// </summary>
    [JsonPropertyName("IsTestingExclusive")]
    public bool IsTestingExclusive { get; set; }

    /// <summary>
    ///     Gets an URL to the website or source code of the plugin.
    /// </summary>
    [JsonPropertyName("RepoUrl")]
    public string? RepoUrl { get; set; }

    /// <summary>
    ///     Gets the version of the game this plugin works with.
    /// </summary>
    [JsonPropertyName("ApplicableVersion")]
    public string? ApplicableVersion { get; set; } = "any";

    /// <summary>
    ///     Gets the API level of this plugin. For the current API level, please see
    ///     <see cref="PluginManager.DalamudApiLevel" />
    ///     for the currently used API level.
    /// </summary>
    [JsonPropertyName("DalamudApiLevel")]
    public int DalamudApiLevel { get; set; }

    /// <summary>
    ///     Gets the number of downloads this plugin has.
    /// </summary>
    [JsonPropertyName("DownloadCount")]
    public long DownloadCount { get; set; }

    /// <summary>
    ///     Gets the last time this plugin was updated.
    /// </summary>
    [JsonPropertyName("LastUpdate")]
    public long LastUpdate { get; set; }

    /// <summary>
    ///     Gets the download link used to install the plugin.
    /// </summary>
    [JsonPropertyName("DownloadLinkInstall")]
    public string DownloadLinkInstall { get; set; }

    /// <summary>
    ///     Gets the download link used to update the plugin.
    /// </summary>
    [JsonPropertyName("DownloadLinkUpdate")]
    public string DownloadLinkUpdate { get; set; }

    /// <summary>
    ///     Gets the download link used to get testing versions of the plugin.
    /// </summary>
    [JsonPropertyName("DownloadLinkTesting")]
    public string DownloadLinkTesting { get; set; }

    /// <summary>
    ///     Gets the load priority for this plugin. Higher values means higher priority. 0 is default priority.
    /// </summary>
    [JsonPropertyName("LoadPriority")]
    public int LoadPriority { get; set; }

    /// <summary>
    ///     Gets a list of screenshot image URLs to show in the plugin installer.
    /// </summary>
    [JsonPropertyName("ImageUrls")]
    public List<string>? ImageUrls { get; set; }

    /// <summary>
    ///     Gets an URL for the plugin's icon.
    /// </summary>
    [JsonPropertyName("IconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>
    ///     Gets a value indicating whether this plugin accepts feedback.
    /// </summary>
    [JsonPropertyName("AcceptsFeedback")]
    public bool AcceptsFeedback { get; set; } = true;

    /// <summary>
    ///     Gets a message that is shown to users when sending feedback.
    /// </summary>
    [JsonPropertyName("FeedbackMessage")]
    public string? FeedbackMessage { get; set; }

    /// <summary>
    ///     Gets a value indicating the webhook URL feedback is sent to.
    /// </summary>
    [JsonPropertyName("FeedbackWebhook")]
    public string? FeedbackWebhook { get; set; }
}