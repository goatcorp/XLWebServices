using System.Text.Json;
using StackExchange.Redis;

namespace XLWebServices.Services;

public class RedisService
{
    private IDatabase _database;

    private const string RedisCountPrefix = "PC1-";
    private const string RedisPrPrefix = "PPR1-";

    public RedisService(ILogger<RedisService> logger, IConfiguration configuration)
    {
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis"));
        _database = redis.GetDatabase();

        logger.LogInformation("Redis is online");
    }

    public async Task SetCachedPlugin(string internalName, string version, PluginInfo info)
    {
        var json = JsonSerializer.Serialize(info);
        await this._database.StringSetAsync($"{RedisPrPrefix}{internalName}-{version}", json);
    }

    public async Task<PluginInfo?> GetCachedPlugin(string internalName, string version)
    {
        var value = await this._database.StringGetAsync($"{RedisPrPrefix}{internalName}-{version}");
        return !value.HasValue ? null : JsonSerializer.Deserialize<PluginInfo>(value.ToString());
    }

    public class PluginInfo
    {
        public long LastUpdate { get; set; }
        public string? PrBody { get; set; }
    }

    public async Task IncrementCount(string internalName)
    {
        await _database.StringIncrementAsync(RedisCountPrefix + internalName);
    }

    public async Task<long> GetCount(string internalName)
    {
        var value = await _database.StringGetAsync(RedisCountPrefix + internalName);

        if (value.IsNullOrEmpty)
        {
            return 0;
        }

        value.TryParse(out long result);
        return result;
    }
}