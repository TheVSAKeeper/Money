using Microsoft.EntityFrameworkCore;
using Money.Business.Interfaces;
using Money.Data.Sharding;
using StackExchange.Redis;
using System.Diagnostics.Metrics;

namespace Money.Api.Services.Cache;

public class CounterCacheService(
    IConnectionMultiplexer redis,
    ShardedDbContextFactory shardFactory,
    ILogger<CounterCacheService> logger) : ICounterCacheService
{
    private const string IncrementScript =
        """
        if redis.call('EXISTS', KEYS[1]) == 0 then
            redis.call('SET', KEYS[1], ARGV[1])
        end
        return redis.call('INCR', KEYS[1])
        """;

    private static readonly Meter Meter = new("Money.Cache");
    private static readonly Counter<long> CacheErrors = Meter.CreateCounter<long>("cache.errors");

    private static readonly IReadOnlyDictionary<string, string> EntityTableMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["operation"] = "operations",
        ["category"] = "categories",
        ["place"] = "places",
        ["fastoperation"] = "fast_operations",
        ["regularoperation"] = "regular_operations",
        ["debt"] = "debts",
        ["debtowner"] = "debt_owners",
        ["car"] = "cars",
        ["carevent"] = "car_events",
    };

    public static string BuildKey(string shardName, int userId, string entityType)
    {
        return $"counter:{entityType}:{shardName}:{userId}";
    }

    public async Task<int?> IncrementAsync(string shardName, int userId, string entityType, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(shardName, userId, entityType);

            if (await db.KeyExistsAsync(key))
            {
                return (int)await db.StringIncrementAsync(key);
            }

            var initialValue = await LoadMaxIdFromDbAsync(shardName, userId, entityType, cancellationToken);
            var result = await db.ScriptEvaluateAsync(IncrementScript, [key], [initialValue]);

            await db.SetAddAsync("counter:index", key);
            return (int)result;
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "counters"), new("cache.operation", "increment"));
            logger.LogWarning(ex, "Redis недоступен при инкременте счётчика {EntityType} для шарда {ShardName}, пользователя {UserId}. Fallback на PostgreSQL", entityType, shardName, userId);
            return null;
        }
    }

    public async Task SetAsync(string shardName, int userId, string entityType, int value, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(shardName, userId, entityType);
            await db.StringSetAsync(key, value - 1);
            await db.SetAddAsync("counter:index", key);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "counters"), new("cache.operation", "set"));
            logger.LogWarning(ex, "Redis недоступен при установке счётчика {EntityType} для шарда {ShardName}, пользователя {UserId}", entityType, shardName, userId);
        }
    }

    private async Task<int> LoadMaxIdFromDbAsync(string shardName, int userId, string entityType, CancellationToken cancellationToken)
    {
        if (!EntityTableMap.TryGetValue(entityType, out var tableName))
        {
            logger.LogWarning("Неизвестный тип сущности для счётчика: {EntityType}", entityType);
            return 0;
        }

        await using var db = shardFactory.Create(shardName);
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COALESCE(MAX(id), 0) FROM {tableName} WHERE user_id = @userId";
        var param = command.CreateParameter();
        param.ParameterName = "userId";
        param.Value = userId;
        command.Parameters.Add(param);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
