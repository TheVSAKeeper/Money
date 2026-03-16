using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Money.ApiClient;
using StackExchange.Redis;

namespace Money.Api.Tests.Tests.PubSub;

public class PubSubTests
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

    [Test]
    public async Task AdminPubSub_ReturnsChannelMetrics()
    {
        _dbClient.Save();

        var result = await _apiClient.Admin.GetPubSubMetrics().IsSuccessWithContent();
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task DebtCreated_PublishesNotificationToRedis()
    {
        _dbClient.Save();

        var shardName = await GetUserShardName();
        var received = new TaskCompletionSource<string>();
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"events:{shardName}:{_user.Id}");
        await subscriber.SubscribeAsync(channel, (_, msg) =>
        {
            if (msg!.ToString().Contains("DebtCreated"))
            {
                received.TrySetResult(msg!);
            }
        });

        try
        {
            await _apiClient.Debts.Create(new()
                {
                    Sum = 1000,
                    OwnerName = "TestOwner",
                    Date = DateTime.UtcNow,
                    TypeId = 1,
                })
                .IsSuccessWithContent();

            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(message, Does.Contain("DebtCreated"));
            Assert.That(message, Does.Contain("1000"));
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    [Test]
    public async Task OperationCreated_OtherUserDoesNotReceive()
    {
        var user2 = _dbClient.WithUser();
        var category2 = user2.WithCategory();
        _dbClient.Save();

        var shardName = await GetUserShardName();
        var received = new List<string>();
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"events:{shardName}:{_user.Id}");
        await subscriber.SubscribeAsync(channel, (_, msg) => received.Add(msg!));

        try
        {
            var apiClient2 = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
            apiClient2.SetUser(user2);
            var request = new OperationsClient.SaveRequest
            {
                CategoryId = category2.Id,
                Sum = 100,
                Date = DateTime.UtcNow,
            };

            await apiClient2.Operations.Create(request).IsSuccessWithContent();

            await Task.Delay(500);
            Assert.That(received, Is.Empty);
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    [Test]
    public async Task OperationCreated_PublishesNotificationToRedis()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var shardName = await GetUserShardName();
        var received = new TaskCompletionSource<string>();
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"events:{shardName}:{_user.Id}");
        await subscriber.SubscribeAsync(channel, (_, msg) => received.TrySetResult(msg!));

        try
        {
            var request = new OperationsClient.SaveRequest
            {
                CategoryId = category.Id,
                Sum = 500,
                Date = DateTime.UtcNow,
            };

            await _apiClient.Operations.Create(request).IsSuccessWithContent();

            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(message, Does.Contain("OperationCreated"));
            Assert.That(message, Does.Contain("500"));
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    [Test]
    public async Task OperationDeleted_PublishesNotificationToRedis()
    {
        var operation = _user.WithOperation();
        _dbClient.Save();

        var shardName = await GetUserShardName();
        var received = new TaskCompletionSource<string>();
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"events:{shardName}:{_user.Id}");
        await subscriber.SubscribeAsync(channel, (_, msg) =>
        {
            if (msg!.ToString().Contains("OperationDeleted"))
            {
                received.TrySetResult(msg!);
            }
        });

        try
        {
            await _apiClient.Operations.Delete(operation.Id).IsSuccess();

            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(message, Does.Contain("OperationDeleted"));
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    private async Task<string> GetUserShardName()
    {
        await using var routingCtx = _dbClient.CreateRoutingDbContext();
        var authUser = await routingCtx.Users.SingleAsync(x => x.UserName == _user.UserName);
        return _dbClient.ShardRouter.ResolveShard(authUser.Id);
    }
}
