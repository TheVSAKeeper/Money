using StackExchange.Redis;

namespace Money.Api.Services.Analytics;

public class OutboxCursorService(IConnectionMultiplexer redis, ILogger<OutboxCursorService> logger)
{
    public async Task<long> GetCursorAsync(string consumer, string shardName)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync(CursorKey(consumer, shardName));
            return value.HasValue ? (long)value : 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read outbox cursor for {Consumer}:{Shard}", consumer, shardName);
            return 0;
        }
    }

    public async Task SetCursorAsync(string consumer, string shardName, long lastProcessedId)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync(CursorKey(consumer, shardName), lastProcessedId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save outbox cursor for {Consumer}:{Shard}", consumer, shardName);
        }
    }

    public async Task<long> GetMinCursorAsync(string[] consumers, string shardName)
    {
        var min = long.MaxValue;

        foreach (var consumer in consumers)
        {
            var cursor = await GetCursorAsync(consumer, shardName);
            min = Math.Min(min, cursor);
        }

        return min == long.MaxValue ? 0 : min;
    }

    private static string CursorKey(string consumer, string shardName)
    {
        return $"outbox:cursor:{consumer}:{shardName}";
    }
}
