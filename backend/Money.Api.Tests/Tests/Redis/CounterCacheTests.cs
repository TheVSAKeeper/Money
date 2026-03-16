using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Money.Api.BackgroundServices;
using Money.Api.Services.Cache;
using Money.ApiClient;
using Money.Business.Interfaces;
using Money.Data.Sharding;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Money.Api.Tests.Tests.Redis;

public class CounterCacheTests
{
    private DatabaseClient _dbClient;
    private TestUser _user;
    private MoneyClient _apiClient;
#pragma warning disable NUnit1032
    private IConnectionMultiplexer _redis;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);
        _redis = Integration.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
    }

    [TearDown]
    public void TearDown()
    {
    }

    [Test]
    public async Task Counter_AtomicUnder1000ConcurrentRequests()
    {
        _dbClient.Save();

        var category = _user.WithCategory();
        _dbClient.Save();

        var ids = new ConcurrentBag<int>();
        var tasks = new List<Task>();

        for (var i = 0; i < 1000; i++)
        {
            var client = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
            client.SetUser(_user);

            tasks.Add(Task.Run(async () =>
            {
                var request = new OperationsClient.SaveRequest
                {
                    Sum = 10,
                    CategoryId = category.Id,
                    Date = DateTime.UtcNow,
                };

                var id = await client.Operations.Create(request).IsSuccessWithContent();
                ids.Add(id);
            }));
        }

        await Task.WhenAll(tasks);

        var idList = ids.ToList();
        Assert.That(idList.Distinct().Count(), Is.EqualTo(idList.Count), "Все ID должны быть уникальными");

        var sorted = idList.OrderBy(x => x).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            Assert.That(sorted[i] - sorted[i - 1], Is.EqualTo(1),
                $"Ожидался непрерывный диапазон: {sorted[i - 1]} → {sorted[i]}");
        }
    }

    [Test]
    public async Task Counter_FallbackToDomainUser_WhenRedisUnavailable()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var badRedis = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:16399" },
            AbortOnConnectFail = false,
            ConnectTimeout = 100,
            SyncTimeout = 100,
        });

        var shardFactory = Integration.ServiceProvider.GetRequiredService<ShardedDbContextFactory>();
        var unavailableCounter = new CounterCacheService(badRedis, shardFactory, NullLogger<CounterCacheService>.Instance);

        int? result = null;
        Assert.DoesNotThrowAsync(async () => result = await unavailableCounter.IncrementAsync("dunduk", _user.Id, "operation"));
        Assert.That(result, Is.Null, "IncrementAsync must return null when Redis is unavailable");

        await badRedis.CloseAsync();

        var request = new OperationsClient.SaveRequest
        {
            Sum = 10,
            CategoryId = category.Id,
            Date = DateTime.UtcNow,
        };

        var operationId = await _apiClient.Operations.Create(request).IsSuccessWithContent();
        Assert.That(operationId, Is.GreaterThan(0), "Operation must be created via DB fallback");
    }

    [Test]
    public async Task Counter_InitializedFromDB_OnColdStart()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var db = _redis.GetDatabase();
        var shardName = await ResolveShardName();

        var counterKey = $"counter:category:{shardName}:{_user.Id}";
        await db.KeyDeleteAsync(counterKey);

        var request = new CategoriesClient.SaveRequest
        {
            Name = "Test Category Cold Start",
            OperationTypeId = (int)category.OperationType,
        };

        await _apiClient.Categories.Create(request).IsSuccessWithContent();

        var exists = await db.KeyExistsAsync(counterKey);
        Assert.That(exists, Is.True);

        var value = (long?)await db.StringGetAsync(counterKey);
        Assert.That(value, Is.Not.Null);
        Assert.That(value!.Value, Is.GreaterThan(0));
    }

    [Test]
    public async Task Counter_SetAsync_SetsCorrectRedisValue()
    {
        _dbClient.Save();

        var shardName = await ResolveShardName();
        var db = _redis.GetDatabase();
        var counterCache = Integration.ServiceProvider.GetRequiredService<ICounterCacheService>();

        await counterCache.SetAsync(shardName, _user.Id, "operation", 999);

        var counterKey = $"counter:operation:{shardName}:{_user.Id}";
        var redisValue = (int?)await db.StringGetAsync(counterKey);
        Assert.That(redisValue, Is.EqualTo(998));

        var nextId = await counterCache.IncrementAsync(shardName, _user.Id, "operation");
        Assert.That(nextId, Is.Not.Null);
        Assert.That(nextId!.Value, Is.EqualTo(999));
    }

    [Test]
    public async Task Counter_SyncedToPostgreSQL_ByBackgroundService()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var shardName = await ResolveShardName();
        var db = _redis.GetDatabase();

        var request = new OperationsClient.SaveRequest
        {
            Sum = 10,
            CategoryId = category.Id,
            Date = DateTime.UtcNow,
        };

        await _apiClient.Operations.Create(request).IsSuccessWithContent();

        var counterKey = $"counter:operation:{shardName}:{_user.Id}";
        var redisValue = (int?)await db.StringGetAsync(counterKey);
        Assert.That(redisValue, Is.Not.Null, "Counter must exist in Redis after operation creation");

        var shardFactory = Integration.ServiceProvider.GetRequiredService<ShardedDbContextFactory>();
        var syncService = new CounterSyncService(_redis,
            shardFactory,
            NullLogger<CounterSyncService>.Instance);

        await syncService.SyncCountersAsync(CancellationToken.None);

        await using var shardContext = shardFactory.Create(shardName);
        var domainUser = await shardContext.DomainUsers.FirstAsync(u => u.Id == _user.Id);

        Assert.That(domainUser.NextOperationId, Is.EqualTo(redisValue!.Value + 1),
            "NextOperationId in DB must match Redis counter + 1 after sync");
    }

    private async Task<string> ResolveShardName()
    {
        await using var routingCtx = _dbClient.CreateRoutingDbContext();
        var authUser = await routingCtx.Users.SingleAsync(x => x.UserName == _user.UserName);
        return _dbClient.ShardRouter.ResolveShard(authUser.Id);
    }
}
