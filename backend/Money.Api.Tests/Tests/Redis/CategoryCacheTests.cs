using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Money.Api.Services.Cache;
using Money.ApiClient;
using Money.Business.Models;
using StackExchange.Redis;

namespace Money.Api.Tests.Tests.Redis;

public class CategoryCacheTests
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
    public async Task TearDown()
    {
        if (_user.Id == 0)
        {
            return;
        }

        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"cache:categories:*:{_user.Id}").ToArray();

        if (keys.Length > 0)
        {
            await db.KeyDeleteAsync(keys);
        }
    }

    [Test]
    public async Task CategoryCache_FallbackToDb_WhenRedisUnavailable()
    {
        _dbClient.Save();

        var firstResult = await _apiClient.Categories.Get().IsSuccessWithContent();
        Assert.That(firstResult, Is.Not.Null);

        var badRedis = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:16399" },
            AbortOnConnectFail = false,
            ConnectTimeout = 100,
            SyncTimeout = 100,
        });

        var unavailableCache = new CategoryCacheService(badRedis, NullLogger<CategoryCacheService>.Instance);

        List<Category>? result = null;
        Assert.DoesNotThrowAsync(async () => result = await unavailableCache.GetAsync("dunduk", 1));
        Assert.That(result, Is.Null, "GetAsync must return null when Redis is unavailable");

        Assert.DoesNotThrowAsync(async () => await unavailableCache.SetAsync("dunduk", 1, []));
        Assert.DoesNotThrowAsync(async () => await unavailableCache.InvalidateAsync("dunduk", 1));

        await badRedis.CloseAsync();
    }

    [Test]
    public async Task CategoryCache_HitOnSecondGet()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Categories.Get().IsSuccessWithContent();
        var result = await _apiClient.Categories.Get().IsSuccessWithContent();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Any(c => c.Id == category.Id), Is.True);
    }

    [Test]
    public async Task CategoryCache_InvalidatedAfterCreate()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Categories.Get().IsSuccessWithContent();

        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keysBefore = server.Keys(pattern: $"cache:categories:*:{_user.Id}").ToList();
        Assert.That(keysBefore, Is.Not.Empty);

        var request = new CategoriesClient.SaveRequest
        {
            Name = "New Category",
            OperationTypeId = (int)category.OperationType,
        };

        await _apiClient.Categories.Create(request).IsSuccessWithContent();

        var keysAfter = server.Keys(pattern: $"cache:categories:*:{_user.Id}").ToList();
        Assert.That(keysAfter, Is.Empty);
    }

    [Test]
    public async Task CategoryCache_InvalidatedAfterDelete()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Categories.Get().IsSuccessWithContent();
        await _apiClient.Categories.Delete(category.Id).IsSuccess();

        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"cache:categories:*:{_user.Id}").ToList();
        Assert.That(keys, Is.Empty);
    }

    [Test]
    public async Task CategoryCache_InvalidatedAfterUpdate()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Categories.Get().IsSuccessWithContent();

        var request = new CategoriesClient.SaveRequest
        {
            Name = "Updated",
            OperationTypeId = (int)category.OperationType,
        };

        await _apiClient.Categories.Update(category.Id, request).IsSuccess();

        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"cache:categories:*:{_user.Id}").ToList();
        Assert.That(keys, Is.Empty);
    }

    [Test]
    public async Task CategoryCache_PopulatedOnFirstGet()
    {
        _dbClient.Save();

        await _apiClient.Categories.Get().IsSuccessWithContent();

        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"cache:categories:*:{_user.Id}").ToList();
        Assert.That(keys, Is.Not.Empty);
    }

    [Test]
    public async Task CategoryCache_TTL_IsApplied()
    {
        _dbClient.Save();

        await _apiClient.Categories.Get().IsSuccessWithContent();

        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"cache:categories:*:{_user.Id}").ToList();

        Assert.That(keys, Is.Not.Empty);

        var ttl = await db.KeyTimeToLiveAsync(keys[0]);
        Assert.That(ttl, Is.Not.Null);
        Assert.That(ttl!.Value.TotalSeconds, Is.GreaterThan(0));
    }
}
