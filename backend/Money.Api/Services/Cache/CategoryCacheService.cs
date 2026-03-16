using Money.Business.Interfaces;
using StackExchange.Redis;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace Money.Api.Services.Cache;

public class CategoryCacheService(IConnectionMultiplexer redis, ILogger<CategoryCacheService> logger) : ICategoryCacheService
{
    private static readonly Meter Meter = new("Money.Cache");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("cache.hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("cache.misses");
    private static readonly Counter<long> CacheErrors = Meter.CreateCounter<long>("cache.errors");

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    public async Task<List<Category>?> GetAsync(string shardName, int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(shardName, userId);
            var value = await db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1, new("cache.type", "categories"), new("cache.operation", "get"));
                return null;
            }

            CacheHits.Add(1, new("cache.type", "categories"), new("cache.operation", "get"));
            return JsonSerializer.Deserialize<List<Category>>((string)value!);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "categories"), new("cache.operation", "get"));
            logger.LogWarning(ex, "Redis недоступен при получении кэша категорий для шарда {ShardName}, пользователя {UserId}", shardName, userId);
            return null;
        }
    }

    public async Task SetAsync(string shardName, int userId, List<Category> data, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(shardName, userId);
            var json = JsonSerializer.Serialize(data);
            await db.StringSetAsync(key, json, Ttl);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "categories"), new("cache.operation", "set"));
            logger.LogWarning(ex, "Redis недоступен при записи кэша категорий для шарда {ShardName}, пользователя {UserId}", shardName, userId);
        }
    }

    public async Task InvalidateAsync(string shardName, int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BuildKey(shardName, userId);
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            CacheErrors.Add(1, new("cache.type", "categories"), new("cache.operation", "invalidate"));
            logger.LogWarning(ex, "Redis недоступен при инвалидации кэша категорий для шарда {ShardName}, пользователя {UserId}", shardName, userId);
        }
    }

    private static string BuildKey(string shardName, int userId)
    {
        return $"cache:categories:{shardName}:{userId}";
    }
}
