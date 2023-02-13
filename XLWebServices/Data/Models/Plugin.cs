using System.ComponentModel.DataAnnotations;

namespace XLWebServices.Data.Models;

public class Plugin
{
    [Key]
    public Guid Id { get; set; }
    
    public string InternalName { get; set; }
    public IList<PluginVersion> VersionHistory = new List<PluginVersion>();
}