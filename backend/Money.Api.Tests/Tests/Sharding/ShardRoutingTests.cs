using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Money.ApiClient;
using Money.Data.Sharding;

namespace Money.Api.Tests.Tests.Sharding;

[TestFixture]
public class ShardRoutingTests
{
    [SetUp]
    public void Setup()
    {
        var scope = Integration.ServiceProvider.CreateScope();
        _router = scope.ServiceProvider.GetRequiredService<ShardRouter>();
        _factory = scope.ServiceProvider.GetRequiredService<ShardedDbContextFactory>();
        _dbClient = Integration.GetDatabaseClient();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
    }

    private ShardRouter _router = null!;
    private ShardedDbContextFactory _factory = null!;
    private DatabaseClient _dbClient = null!;
    private MoneyClient _apiClient = null!;

    [Test]
    public void ConsistentHash_SameAuthUserId_AlwaysSameShard()
    {
        var authUserId = Guid.NewGuid();
        var shard1 = _router.ResolveShard(authUserId);
        var shard2 = _router.ResolveShard(authUserId);
        Assert.That(shard1, Is.EqualTo(shard2));
    }

    [Test]
    public void ConsistentHash_SameIntUserId_AlwaysSameShard()
    {
        var shard1 = _router.ResolveShard(42);
        var shard2 = _router.ResolveShard(42);
        Assert.That(shard1, Is.EqualTo(shard2));
    }

    [Test]
    public void ConsistentHash_GuidDistribution_ReasonablyBalanced()
    {
        var counts = new Dictionary<string, int>();

        for (var i = 0; i < 1000; i++)
        {
            var shard = _router.ResolveShard(Guid.NewGuid());
            counts.TryAdd(shard, 0);
            counts[shard]++;
        }

        var expected = 1000.0 / counts.Count;

        foreach (var (shard, count) in counts)
        {
            var deviation = Math.Abs(count - expected) / expected;

            Assert.That(deviation, Is.LessThan(0.20),
                $"Shard {shard} has {count} users, expected ~{expected:F0} (deviation {deviation:P0} > 20%)");
        }
    }

    [Test]
    public void ConsistentHash_IntDistribution_ReasonablyBalanced()
    {
        var counts = new Dictionary<string, int>();

        for (var i = 0; i < 1000; i++)
        {
            var shard = _router.ResolveShard(i);
            counts.TryAdd(shard, 0);
            counts[shard]++;
        }

        var expected = 1000.0 / counts.Count;

        foreach (var (shard, count) in counts)
        {
            var deviation = Math.Abs(count - expected) / expected;

            Assert.That(deviation, Is.LessThan(0.20),
                $"Shard {shard} has {count} users, expected ~{expected:F0} (deviation {deviation:P0} > 20%)");
        }
    }

    [Test]
    public void DifferentUsers_CanBeOnDifferentShards()
    {
        var shards = Enumerable.Range(0, 10000)
            .Select(_ => _router.ResolveShard(Guid.NewGuid()))
            .Distinct()
            .ToList();

        Assert.That(shards.Count, Is.GreaterThan(1),
            "Expected users to be distributed across multiple shards");
    }

    [Test]
    public async Task UserData_CreatedOnCorrectShard()
    {
        var user = _dbClient.WithUser();
        var category = user.WithCategory();
        _dbClient.Save();

        _apiClient.SetUser(user);

        using var routingCtx = _dbClient.CreateRoutingDbContext();
        var authUser = routingCtx.Users.Single(x => x.UserName == user.UserName);
        var expectedShard = _router.ResolveShard(authUser.Id);

        foreach (var shardName in _factory.ShardNames)
        {
            await using var shardCtx = _factory.Create(shardName);
            var hasUser = shardCtx.DomainUsers.Any(x => x.AuthUserId == authUser.Id);
            var hasCat = shardCtx.Categories.Any(x => x.UserId == user.Id && x.Name == category.Name);

            Assert.That(hasUser, Is.EqualTo(shardName == expectedShard),
                $"DomainUser on shard {shardName}: expected {shardName == expectedShard}");

            Assert.That(hasCat, Is.EqualTo(shardName == expectedShard),
                $"Category on shard {shardName}: expected {shardName == expectedShard}");
        }
    }

    [Test]
    public async Task TwoUsers_CanHaveDataOnDifferentShards()
    {
        var user1 = _dbClient.WithUser();
        user1.WithCategory();
        var user2 = _dbClient.WithUser();
        user2.WithCategory();
        _dbClient.Save();

        using var routingCtx = _dbClient.CreateRoutingDbContext();
        var authUser1 = routingCtx.Users.Single(x => x.UserName == user1.UserName);
        var authUser2 = routingCtx.Users.Single(x => x.UserName == user2.UserName);

        var shard1 = _router.ResolveShard(authUser1.Id);
        var shard2 = _router.ResolveShard(authUser2.Id);

        await using var ctx1 = _factory.Create(shard1);
        Assert.That(await ctx1.DomainUsers.AnyAsync(x => x.Id == user1.Id), Is.True);

        await using var ctx2 = _factory.Create(shard2);
        Assert.That(await ctx2.DomainUsers.AnyAsync(x => x.Id == user2.Id), Is.True);
    }

    [Test]
    public async Task UserApi_WorksAfterRegistration()
    {
        var user = _dbClient.WithUser();
        _dbClient.Save();

        _apiClient.SetUser(user);

        var categories = await _apiClient.Categories.Get();
        categories.IsSuccess();
    }
}
