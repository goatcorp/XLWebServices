using StackExchange.Redis;

namespace XLWebServices.Services;

public class RedisService
{
    private IDatabase _database;

    private const string RedisPrefix = "PC1-";

    public RedisService(ILogger<RedisService> logger, IConfiguration configuration)
    {
        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis"));
        _database = redis.GetDatabase();

        logger.LogInformation("Redis is online");
    }

    public async Task Increment(string internalName)
    {
        await _database.StringIncrementAsync(RedisPrefix + internalName);
    }

    public async Task<long> Get(string internalName)
    {
        var value = await _database.StringGetAsync(RedisPrefix + internalName);

        if (value.IsNullOrEmpty)
        {
            return 0;
        }

        value.TryParse(out long result);
        return result;
    }
}