using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Money.Api.Services.Cache;
using Money.ApiClient;
using Money.Business.Models;
using StackExchange.Redis;

namespace Money.Api.Tests.Tests.Redis;

public class OperationCacheTests
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
    public async Task OperationCache_CachesResult()
    {
        var operation = _user.WithOperation();
        _dbClient.Save();

        var filter = new OperationsClient.OperationFilterDto
        {
            DateFrom = DateTime.UtcNow.AddMonths(-1),
            DateTo = DateTime.UtcNow.AddMonths(1),
        };

        var result = await _apiClient.Operations.Get(filter).IsSuccessWithContent();
        Assert.That(result.Any(o => o.Id == operation.Id), Is.True);

        var (_, indexKey, db) = await GetCacheContext();
        Assert.That(await db.KeyExistsAsync(indexKey), Is.True);

        var members = await db.SetMembersAsync(indexKey);
        Assert.That(members, Is.Not.Empty);
    }

    [Test]
    public async Task OperationCache_DifferentFilters_DifferentKeys()
    {
        _user.WithOperation();
        _dbClient.Save();

        var filter1 = new OperationsClient.OperationFilterDto { DateFrom = new DateTime(2026, 1, 1) };
        var filter2 = new OperationsClient.OperationFilterDto { DateFrom = new DateTime(2025, 1, 1) };

        await _apiClient.Operations.Get(filter1).IsSuccessWithContent();
        await _apiClient.Operations.Get(filter2).IsSuccessWithContent();

        var (_, indexKey, db) = await GetCacheContext();
        var cacheKeys = await db.SetMembersAsync(indexKey);

        Assert.That(cacheKeys, Has.Length.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task OperationCache_FallbackToDb_WhenRedisUnavailable()
    {
        _user.WithOperation();
        _dbClient.Save();

        var filter = new OperationsClient.OperationFilterDto
        {
            DateFrom = DateTime.UtcNow.AddMonths(-1),
            DateTo = DateTime.UtcNow.AddMonths(1),
        };

        var result = await _apiClient.Operations.Get(filter).IsSuccessWithContent();
        Assert.That(result, Is.Not.Empty);

        var badRedis = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:16399" },
            AbortOnConnectFail = false,
            ConnectTimeout = 100,
            SyncTimeout = 100,
        });

        var unavailableCache = new OperationCacheService(badRedis, NullLogger<OperationCacheService>.Instance);
        var operationFilter = new OperationFilter();

        List<Operation>? cached = null;
        Assert.DoesNotThrowAsync(async () => cached = await unavailableCache.GetAsync("dunduk", 1, operationFilter));
        Assert.That(cached, Is.Null, "GetAsync must return null when Redis is unavailable");

        Assert.DoesNotThrowAsync(async () => await unavailableCache.SetAsync("dunduk", 1, operationFilter, []));
        Assert.DoesNotThrowAsync(async () => await unavailableCache.InvalidateAllForUserAsync("dunduk", 1));

        await badRedis.CloseAsync();
    }

    [Test]
    public async Task OperationCache_IndexCleanedAfterInvalidation()
    {
        var operation = _user.WithOperation();
        _dbClient.Save();

        var filter = new OperationsClient.OperationFilterDto
        {
            DateFrom = DateTime.UtcNow.AddMonths(-1),
            DateTo = DateTime.UtcNow.AddMonths(1),
        };

        await _apiClient.Operations.Get(filter).IsSuccessWithContent();

        var (_, indexKey, db) = await GetCacheContext();

        var dataKeys = await db.SetMembersAsync(indexKey);
        Assert.That(dataKeys, Is.Not.Empty, "Data keys should be in index before delete");

        await _apiClient.Operations.Delete(operation.Id).IsSuccess();

        Assert.That(await db.KeyExistsAsync(indexKey), Is.False, "Index must be deleted after operation delete");

        foreach (var dataKey in dataKeys)
        {
            Assert.That(await db.KeyExistsAsync((string)dataKey!), Is.False, $"Data key {dataKey} must be deleted");
        }
    }

    [Test]
    public async Task OperationCache_InvalidatedAfterCreate()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var filter = new OperationsClient.OperationFilterDto
        {
            DateFrom = DateTime.UtcNow.AddMonths(-1),
            DateTo = DateTime.UtcNow.AddMonths(1),
        };

        await _apiClient.Operations.Get(filter).IsSuccessWithContent();

        var (_, indexKey, db) = await GetCacheContext();
        Assert.That(await db.KeyExistsAsync(indexKey), Is.True, "Index should exist after GET");

        var request = new OperationsClient.SaveRequest
        {
            Sum = 100,
            CategoryId = category.Id,
            Date = DateTime.UtcNow,
        };

        await _apiClient.Operations.Create(request).IsSuccessWithContent();

        Assert.That(await db.KeyExistsAsync(indexKey), Is.False, "Index must be deleted after Create");
    }

    private async Task<(string shardName, string indexKey, IDatabase db)> GetCacheContext()
    {
        await using var routingCtx = _dbClient.CreateRoutingDbContext();
        var authUser = await routingCtx.Users.SingleAsync(x => x.UserName == _user.UserName);
        var shardName = _dbClient.ShardRouter.ResolveShard(authUser.Id);
        var indexKey = $"cache:operations:index:{shardName}:{_user.Id}";
        return (shardName, indexKey, _redis.GetDatabase());
    }
}
