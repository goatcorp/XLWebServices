using System.ComponentModel.DataAnnotations;

namespace XLWebServices.Data.Models;

public class PluginVersion
{
    [Key]
    public Guid Id { get; set; }
    
    public Plugin Plugin { get; set; }
    public string Version { get; set; }
    public string Dip17Track { get; set; }
    public string? Changelog { get; set; }
    public DateTime PublishedAt { get; set; }
    public int? PrNumber { get; set; }
    public string? PublishedBy { get; set; }
    public bool IsHidden { get; set; }
    public bool? IsInitialRelease { get; set; }
    public TimeSpan? TimeToMerge { get; set; }
    public int? DiffLinesAdded { get; set; }
    public int? DiffLinesRemoved { get; set; }
}