using Microsoft.EntityFrameworkCore;
using XLWebServices.Data.Models;

namespace XLWebServices.Data;

public class WsDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<Plugin> Plugins { get; set; } = null!;
    public DbSet<PluginVersion> PluginVersions { get; set; } = null!;

    public WsDbContext(IConfiguration config)
    {
        _dbPath = config["DatabasePath"]!;
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plugin>()
            .HasMany(c => c.VersionHistory)
            .WithOne(e => e.Plugin)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<PluginVersion>()
            .Property(x => x.Version)
            .HasConversion(
                v => v.ToString(),
                v => Version.Parse(v));
    }
}