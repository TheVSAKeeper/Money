using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Money.Api.Services.Locks;
using Money.ApiClient;
using Money.Common.Exceptions;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Money.Api.Tests.Tests.Redis;

public class DistributedLockTests
{
#pragma warning disable NUnit1032
    private IConnectionMultiplexer _redis = null!;
#pragma warning restore NUnit1032
    private RedisLockService _lockService = null!;

    [SetUp]
    public void Setup()
    {
        _redis = Integration.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        _lockService = new(_redis, NullLogger<RedisLockService>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = await server.KeysAsync(pattern: "lock:test:*").ToArrayAsync();

        if (keys.Length > 0)
        {
            await db.KeyDeleteAsync(keys);
        }
    }

    [Test]
    public async Task AcquireAsync_AlreadyHeld_ThrowsLockNotAcquiredException()
    {
        const string Key = "lock:test:already-held";

        await using var first = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(10));

        Assert.ThrowsAsync<LockNotAcquiredException>(async () => await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(1), 1));
    }

    [Test]
    public async Task AcquireAsync_DifferentKeys_DoNotConflict()
    {
        const string Key1 = "lock:test:different-keys-1";
        const string Key2 = "lock:test:different-keys-2";

        await using var handle1 = await _lockService.AcquireAsync(Key1, TimeSpan.FromSeconds(5));
        await using var handle2 = await _lockService.AcquireAsync(Key2, TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(handle1, Is.Not.Null);
            Assert.That(handle2, Is.Not.Null);
        }
    }

    [Test]
    public async Task AcquireAsync_RedisUnavailable_ReturnsNoOpHandle()
    {
        var badRedis = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:16399" },
            AbortOnConnectFail = false,
            ConnectTimeout = 100,
            SyncTimeout = 100,
        });

        var unavailableService = new RedisLockService(badRedis, NullLogger<RedisLockService>.Instance);

        IAsyncDisposable? handle = null;

        Assert.DoesNotThrowAsync(async () =>
            handle = await unavailableService.AcquireAsync("lock:test:unavailable", TimeSpan.FromSeconds(5)));

        Assert.That(handle, Is.Not.Null);

        Assert.DoesNotThrowAsync(async () => await handle!.DisposeAsync());

        await badRedis.CloseAsync();
    }

    [Test]
    public async Task AcquireAsync_Success_ReturnsLockHandle()
    {
        var db = _redis.GetDatabase();
        const string Key = "lock:test:acquire-success";

        await using var handle = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(5));

        Assert.That(handle, Is.Not.Null);
        var exists = await db.KeyExistsAsync(Key);
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task Lock_OnlyOwnerCanRelease()
    {
        var db = _redis.GetDatabase();
        const string Key = "lock:test:only-owner";

        var handle = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(10));

        var wrongValue = Guid.NewGuid().ToString();
        const string ReleaseScript = """
                                     if redis.call("get", KEYS[1]) == ARGV[1] then
                                         return redis.call("del", KEYS[1])
                                     else
                                         return 0
                                     end
                                     """;

        var result = (int)await db.ScriptEvaluateAsync(ReleaseScript, [Key], [wrongValue]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Zero, "Wrong owner must not release the lock");
            Assert.That(await db.KeyExistsAsync(Key), Is.True, "Lock key must still exist");
        }

        await handle.DisposeAsync();
        Assert.That(await db.KeyExistsAsync(Key), Is.False, "Lock must be released by owner");
    }

    [Test]
    public async Task Lock_PreventsParallelCounterDuplication()
    {
        var dbClient = Integration.GetDatabaseClient();
        var user = dbClient.WithUser();
        var category = user.WithCategory();
        dbClient.Save();

        var ids = new ConcurrentBag<int>();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(async () =>
            {
                var client = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
                client.SetUser(user);

                var id = await client.Operations.Create(new()
                    {
                        Sum = 1,
                        CategoryId = category.Id,
                        Date = DateTime.UtcNow,
                    })
                    .IsSuccessWithContent();

                ids.Add(id);
            }));

        await Task.WhenAll(tasks);

        var idList = ids.ToList();
        Assert.That(idList.Distinct().Count(), Is.EqualTo(100), "All 100 operation IDs must be unique");
    }

    [Test]
    public async Task Lock_ReleasedAfterDispose_KeyGoneFromRedis()
    {
        var db = _redis.GetDatabase();
        const string Key = "lock:test:release";

        var handle = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(10));
        Assert.That(await db.KeyExistsAsync(Key), Is.True);

        await handle.DisposeAsync();
        Assert.That(await db.KeyExistsAsync(Key), Is.False);
    }

    [Test]
    public async Task Lock_ReleasedAfterTTL_CanBeAcquiredByAnother()
    {
        const string Key = "lock:test:ttl-expiry";

        _ = await _lockService.AcquireAsync(Key, TimeSpan.FromMilliseconds(500));

        await Task.Delay(700);

        await using var second = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(5));
        Assert.That(second, Is.Not.Null);
    }

    [Test]
    public async Task Lock_RetrySucceedsAfterRelease()
    {
        const string Key = "lock:test:retry-success";

        var firstHandle = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(10));

        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            await firstHandle.DisposeAsync();
        });

        await using var second = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(5));
        Assert.That(second, Is.Not.Null);
    }

    [Test]
    public async Task Lock_StatsAcquiredIncremented_OnSuccess()
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync("lock:stats:acquired");

        const string Key = "lock:test:stats-acquired";
        await using var handle = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(5));

        var acquired = (long?)await db.StringGetAsync("lock:stats:acquired");
        Assert.That(acquired, Is.GreaterThan(0));
    }

    [Test]
    public async Task Lock_StatsFailedIncremented_OnTimeout()
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync("lock:stats:failed");

        const string Key = "lock:test:stats-failed";
        await using var first = await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(10));

        Assert.ThrowsAsync<LockNotAcquiredException>(async () => await _lockService.AcquireAsync(Key, TimeSpan.FromSeconds(1), 1));

        var failed = (long?)await db.StringGetAsync("lock:stats:failed");
        Assert.That(failed, Is.GreaterThan(0));
    }
}
