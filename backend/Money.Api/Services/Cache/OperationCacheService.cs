using Money.Business.Interfaces;
using StackExchange.Redis;
using System.Diagnostics.Metrics;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace Money.Api.Services.Cache;

public class OperationCacheService(IConnectionMultiplexer redis, ILogger<OperationCacheService> logger) : IOperationCacheService
{
    private static readonly Meter Meter = new("Money.Cache");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("cache.hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("cache.misses");
    private static readonly Counter<long> CacheErrors = Meter.CreateCounter<long>("cache.errors");

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<List<Operation>?> GetAsync(string shardName, int userId, OperationFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(shardName, userId, filter);
            var value = await db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1, new("cache.type", "operations"), new("cache.operation", "get"));
                return null;
            }

            CacheHits.Add(1, new("cache.type", "operations"), new("cache.operation", "get"));
            return JsonSerializer.Deserialize<List<Operation>>((string)value!);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "operations"), new("cache.operation", "get"));
            logger.LogWarning(ex, "Redis недоступен при получении кэша операций для шарда {ShardName}, пользователя {UserId}", shardName, userId);
            return null;
        }
    }

    public async Task SetAsync(string shardName, int userId, OperationFilter filter, List<Operation> data, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(shardName, userId, filter);
            var indexKey = BuildIndexKey(shardName, userId);
            var json = JsonSerializer.Serialize(data);

            var batch = db.CreateBatch();
            var setTask = batch.StringSetAsync(key, json, Ttl);
            var addTask = batch.SetAddAsync(indexKey, key);
            batch.Execute();

            await Task.WhenAll(setTask, addTask);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "operations"), new("cache.operation", "set"));
            logger.LogWarning(ex, "Redis недоступен при записи кэша операций для шарда {ShardName}, пользователя {UserId}", shardName, userId);
        }
    }

    public async Task InvalidateAllForUserAsync(string shardName, int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var indexKey = BuildIndexKey(shardName, userId);
            var keys = await db.SetMembersAsync(indexKey);

            if (keys.Length > 0)
            {
                var redisKeys = keys
                    .Select(k => (RedisKey)(string)k!)
                    .Append(indexKey)
                    .ToArray();

                await db.KeyDeleteAsync(redisKeys);
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "operations"), new("cache.operation", "invalidate"));
            logger.LogWarning(ex, "Redis недоступен при инвалидации кэша операций для шарда {ShardName}, пользователя {UserId}", shardName, userId);
        }
    }

    private static string BuildKey(string shardName, int userId, OperationFilter filter)
    {
        var filterJson = JsonSerializer.Serialize(filter);
        var filterBytes = Encoding.UTF8.GetBytes(filterJson);
        var hash = XxHash128.HashToUInt128(filterBytes);
        return $"cache:operations:{shardName}:{userId}:{hash}";
    }

    private static string BuildIndexKey(string shardName, int userId)
    {
        return $"cache:operations:index:{shardName}:{userId}";
    }
}
