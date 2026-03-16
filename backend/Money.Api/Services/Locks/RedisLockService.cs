using Money.Business.Interfaces;
using Money.Common.Exceptions;
using StackExchange.Redis;

namespace Money.Api.Services.Locks;

public class RedisLockService(
    IConnectionMultiplexer redis,
    ILogger<RedisLockService> logger) : IDistributedLockService
{
    private const string ReleaseScript =
        """
        if redis.call("get", KEYS[1]) == ARGV[1] then
            return redis.call("del", KEYS[1])
        else
            return 0
        end
        """;

    private const string StatsKeyAcquired = "lock:stats:acquired";
    private const string StatsKeyFailed = "lock:stats:failed";

    public async Task<IAsyncDisposable> AcquireAsync(
        string key,
        TimeSpan ttl,
        int retryCount = 3,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var lockValue = Guid.NewGuid().ToString();

            for (var attempt = 0; attempt <= retryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var acquired = await db.StringSetAsync(key, lockValue, ttl, When.NotExists);

                if (acquired)
                {
                    logger.LogDebug("Lock acquired: {Key}", key);
                    await db.StringIncrementAsync(StatsKeyAcquired);
                    return new LockHandle(db, key, lockValue, logger);
                }

                if (attempt >= retryCount)
                {
                    continue;
                }

                var delayMs = (int)(50 * Math.Pow(2, attempt));
                var jitter = (int)(delayMs * 0.2 * (Random.Shared.NextDouble() * 2 - 1));
                await Task.Delay(delayMs + jitter, cancellationToken);
            }

            logger.LogWarning("Lock not acquired after {RetryCount} retries: {Key}", retryCount, key);
            await db.StringIncrementAsync(StatsKeyFailed);
            throw new LockNotAcquiredException(key, retryCount);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable, proceeding without lock for key: {Key}", key);
            return NoOpLockHandle.Instance;
        }
    }

    private sealed class LockHandle(IDatabase db, string key, string lockValue, ILogger logger) : IAsyncDisposable
    {
        private readonly long _acquiredAt = Environment.TickCount64;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseScript, [key], [lockValue]);
                var heldMs = Environment.TickCount64 - _acquiredAt;
                logger.LogDebug("Lock released: {Key} (held {HeldMs}ms)", key, heldMs);
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
            {
                logger.LogWarning(ex, "Redis unavailable on lock release: {Key}", key);
            }
        }
    }

    private sealed class NoOpLockHandle : IAsyncDisposable
    {
        public static readonly NoOpLockHandle Instance = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
