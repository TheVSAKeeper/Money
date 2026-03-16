using Microsoft.EntityFrameworkCore;
using Money.Api.Services.Cache;
using Money.Data.Entities;
using Money.Data.Sharding;
using StackExchange.Redis;

namespace Money.Api.BackgroundServices;

/// <summary>
/// Write-Behind: синхронизирует Redis-счётчики обратно в PostgreSQL раз в минуту.
/// При краше Redis: следующий IncrementAsync загрузит MAX(id) из таблицы — данные не потеряются.
/// </summary>
public class CounterSyncService(
    IConnectionMultiplexer redis,
    ShardedDbContextFactory shardFactory,
    ILogger<CounterSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(1);

    // entityType (из ключа) → property setter для DomainUser
    private static readonly IReadOnlyDictionary<string, Action<DomainUser, int>> EntitySetters =
        new Dictionary<string, Action<DomainUser, int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["operation"] = (u, v) => u.NextOperationId = v,
            ["category"] = (u, v) => u.NextCategoryId = v,
            ["place"] = (u, v) => u.NextPlaceId = v,
            ["fastoperation"] = (u, v) => u.NextFastOperationId = v,
            ["regularoperation"] = (u, v) => u.NextRegularOperationId = v,
            ["debt"] = (u, v) => u.NextDebtId = v,
            ["debtowner"] = (u, v) => u.NextDebtOwnerId = v,
            ["car"] = (u, v) => u.NextCarId = v,
            ["carevent"] = (u, v) => u.NextCarEventId = v,
        };

    internal async Task SyncCountersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var db = redis.GetDatabase();
            var counterKeys = await db.SetMembersAsync("counter:index");

            if (counterKeys.Length == 0)
            {
                return;
            }

            logger.LogDebug("Синхронизация {Count} счётчиков из Redis в PostgreSQL", counterKeys.Length);

            var byShardName = counterKeys
                .Select(k => ParseKey((string)k!))
                .Where(x => x != null)
                .GroupBy(x => x!.ShardName);

            foreach (var shardGroup in byShardName)
            {
                await SyncShardAsync(db, shardGroup.Key, shardGroup.Select(x => x!).ToList(), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен во время синхронизации счётчиков");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при синхронизации счётчиков");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SyncInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncCountersAsync(stoppingToken);
        }
    }

    private static ParsedKey? ParseKey(string key)
    {
        // counter:{entityType}:{shardName}:{userId}
        var parts = key.Split(':');

        if (parts.Length != 4 || parts[0] != "counter")
        {
            return null;
        }

        if (!int.TryParse(parts[3], out var userId))
        {
            return null;
        }

        return new(parts[1], parts[2], userId);
    }

    private async Task SyncShardAsync(IDatabase db, string shardName, List<ParsedKey> keys, CancellationToken cancellationToken)
    {
        await using var context = shardFactory.Create(shardName);

        foreach (var parsedKey in keys)
        {
            if (!EntitySetters.TryGetValue(parsedKey.EntityType, out var setter))
            {
                continue;
            }

            var redisKey = CounterCacheService.BuildKey(parsedKey.ShardName, parsedKey.UserId, parsedKey.EntityType);
            var value = (int?)await db.StringGetAsync(redisKey);

            if (value == null)
            {
                continue;
            }

            var domainUser = await context.DomainUsers
                .FirstOrDefaultAsync(u => u.Id == parsedKey.UserId, cancellationToken);

            if (domainUser == null)
            {
                continue;
            }

            setter(domainUser, value.Value + 1);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed record ParsedKey(string EntityType, string ShardName, int UserId);
}
