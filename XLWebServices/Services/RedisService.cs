using System.Text.Json;
using StackExchange.Redis;

namespace XLWebServices.Services;

public class RedisService
{
    public IDatabase Database;

    private const string RedisCountPrefix = "PC1-";
    private const string RedisEndCountPrefix = "PEC1-";
    private const string RedisPrPrefix = "PPR1-";

    public RedisService(ILogger<RedisService> logger, IConfiguration configuration)
    {
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis"));
        this.Database = redis.GetDatabase();

        logger.LogInformation("Redis is online");
    }

    public async Task SetCachedPlugin(string internalName, string version, PluginInfo info)
    {
        var json = JsonSerializer.Serialize(info);
        await this.Database.StringSetAsync($"{RedisPrPrefix}{internalName}-{version}", json);
    }

    public async Task<PluginInfo?> GetCachedPlugin(string internalName, string version)
    {
        var value = await this.Database.StringGetAsync($"{RedisPrPrefix}{internalName}-{version}");
        return !value.HasValue ? null : JsonSerializer.Deserialize<PluginInfo>(value.ToString());
    }

    public class PluginInfo
    {
        public long LastUpdate { get; set; }
        public string? PrBody { get; set; }
    }

    public async Task IncrementCount(string internalName)
    {
        await this.Database.StringIncrementAsync(RedisCountPrefix + internalName);
    }

    public async Task<long> GetCount(string internalName)
    {
        var value = await this.Database.StringGetAsync(RedisCountPrefix + internalName);

        if (value.IsNullOrEmpty)
        {
            return 0;
        }

        value.TryParse(out long result);
        return result;
    }

    public async Task<long> IncrementEndCount(string internalName)
    {
        return await this.Database.StringIncrementAsync(RedisEndCountPrefix + internalName);
    }

    public async Task<long> GetEndCount(string internalName)
    {
        var value = await this.Database.StringGetAsync(RedisEndCountPrefix + internalName);

        if (value.IsNullOrEmpty)
        {
            return 0;
        }

        value.TryParse(out long result);
        return result;
    }
}