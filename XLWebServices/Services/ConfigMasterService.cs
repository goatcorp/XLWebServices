namespace XLWebServices.Services;

public class ConfigMasterService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ConfigMasterService> _logger;

    public string Dip17RepoOwner { get; private set; } = null!;
    public string Dip17RepoName { get; private set; } = null!;
    
    public string Dip17DistRepoOwner { get; private set; } = null!;
    public string Dip17DistRepoName { get; private set; } = null!;
    
    public string PlogonApiKey { get; private set; } = null!;

    public ConfigMasterService(IConfiguration config, ILogger<ConfigMasterService> logger)
    {
        _config = config;
        _logger = logger;
        
        AssignAll();
    }

    private void AssignAll()
    {
        Dip17RepoOwner = AssignCritical("GitHub:PluginRepositoryD17:Owner");
        Dip17RepoName = AssignCritical("GitHub:PluginRepositoryD17:Name");
        
        Dip17DistRepoOwner = AssignCritical("GitHub:PluginDistD17:Owner");
        Dip17DistRepoName = AssignCritical("GitHub:PluginDistD17:Name");

        PlogonApiKey = AssignCritical("PlogonApiKey");
    }

    private string AssignCritical(string name)
    {
        var value = _config[name];
        if (string.IsNullOrEmpty(value))
            throw new Exception($"Critical config value not preset: {name}");

        return value;
    }

    private string? AssignOptional(string name)
    {
        var value = _config[name];
        if (string.IsNullOrEmpty(value))
            _logger.LogWarning("Optional config value not preset: {Name}", name);

        return value;
    }
}