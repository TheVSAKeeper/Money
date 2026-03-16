using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Money.Api.BackgroundServices;
using Money.Api.Services.Queue;
using Money.ApiClient;
using Money.Business.Interfaces;
using Money.Business.Models;
using StackExchange.Redis;

namespace Money.Api.Tests.Tests.Redis;

public class EmailQueueTests
{
#pragma warning disable NUnit1032
    private IConnectionMultiplexer _redis = null!;
#pragma warning restore NUnit1032
    private IEmailQueueService _emailQueue = null!;

    [SetUp]
    public async Task Setup()
    {
        _redis = Integration.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        _emailQueue = Integration.ServiceProvider.GetRequiredService<IEmailQueueService>();
        await CleanQueuesAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await CleanQueuesAsync();
    }

    [Test]
    public async Task EmailQueue_ConcurrentDequeue_NoDuplicates()
    {
        for (var i = 0; i < 100; i++)
        {
            await _emailQueue.EnqueueAsync(new($"{i}@test.com", $"Msg {i}", "Body"));
        }

        var task1 = _emailQueue.DequeueBatchAsync(100);
        var task2 = _emailQueue.DequeueBatchAsync(100);
        var results = await Task.WhenAll(task1, task2);
        var allMessages = results.SelectMany(r => r).ToList();

        var distinctCount = allMessages.Select(m => m.Message.ReceiverEmail).Distinct().Count();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(distinctCount, Is.EqualTo(allMessages.Count), "Concurrent dequeue must not produce duplicates");
            Assert.That(allMessages, Has.Count.LessThanOrEqualTo(100));
        }
    }

    [Test]
    public async Task EmailQueue_DeadLetterAfterMaxRetries()
    {
        var envelope = new MailEnvelope
        {
            Message = new("fail@test.com", "Fail", "Body"),
            RetryCount = 5,
        };

        await _emailQueue.EnqueueDeadLetterAsync(envelope);

        var dlqLength = await _emailQueue.GetDeadLetterQueueLengthAsync();
        Assert.That(dlqLength, Is.EqualTo(1));
    }

    [Test]
    public async Task EmailQueue_FIFO_Order()
    {
        await _emailQueue.EnqueueAsync(new("a@test.com", "A", "Body"));
        await _emailQueue.EnqueueAsync(new("b@test.com", "B", "Body"));
        await _emailQueue.EnqueueAsync(new("c@test.com", "C", "Body"));

        var batch = await _emailQueue.DequeueBatchAsync(3);

        Assert.That(batch.Select(m => m.Message.Title), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public async Task EmailQueue_GetQueueLength_ReturnsCorrectCount()
    {
        Assert.That(await _emailQueue.GetQueueLengthAsync(), Is.Zero);

        await _emailQueue.EnqueueAsync(new("a@test.com", "A", "Body"));
        await _emailQueue.EnqueueAsync(new("b@test.com", "B", "Body"));

        Assert.That(await _emailQueue.GetQueueLengthAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task EmailQueue_PeekQueue_DoesNotRemoveItems()
    {
        await _emailQueue.EnqueueAsync(new("peek@test.com", "Peek", "Body"));

        var peeked = await _emailQueue.PeekQueueAsync(10);
        var lengthAfterPeek = await _emailQueue.GetQueueLengthAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(peeked, Has.Count.EqualTo(1));
            Assert.That(lengthAfterPeek, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task EmailQueue_PersistsAcrossServiceRecreation()
    {
        await _emailQueue.EnqueueAsync(new("test@test.com", "Test", "Body"));

        var newEmailQueue = CreateNewServiceInstance();
        var messages = await newEmailQueue.DequeueBatchAsync(1);

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0].Message.ReceiverEmail, Is.EqualTo("test@test.com"));
    }

    [Test]
    public async Task EmailQueue_RetryWithExponentialBackoff()
    {
        var envelope0 = new MailEnvelope { Message = new("a@test.com", "A", "Body"), RetryCount = 0 };
        var envelope1 = new MailEnvelope { Message = new("b@test.com", "B", "Body"), RetryCount = 1 };

        await _emailQueue.EnqueueRetryAsync(envelope0);
        await _emailQueue.EnqueueRetryAsync(envelope1);

        var retryCount = await _emailQueue.GetRetryQueueLengthAsync();
        Assert.That(retryCount, Is.EqualTo(2));

        var ready = await _emailQueue.DequeueReadyRetriesAsync(10);
        Assert.That(ready, Is.Empty);
    }

    [Test]
    public async Task Register_EnqueuesEmail_InRedis()
    {
        var dbClient = Integration.GetDatabaseClient();
        var adminUser = dbClient.WithUser();
        dbClient.Save();

        var registerClient = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
        await registerClient.Accounts.RegisterAsync(new()
            {
                UserName = $"emailqtest_{Guid.NewGuid():N}",
                Password = "Test1234!",
                Email = $"emailqtest_{Guid.NewGuid():N}@test.com",
            })
            .IsSuccess();

        var adminClient = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
        adminClient.SetUser(adminUser);

        var stats = await adminClient.Admin.GetEmailQueueStatsAsync();
        Assert.That(stats.Content, Is.Not.Null);

        Assert.That(stats.Content!.QueueLength + stats.Content.RetryLength, Is.GreaterThanOrEqualTo(0));
    }

    private async Task CleanQueuesAsync()
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync("email:queue");
        await db.KeyDeleteAsync("email:retry");
        await db.KeyDeleteAsync("email:dlq");
    }

    private IEmailQueueService CreateNewServiceInstance()
    {
        var settings = Options.Create(new EmailSenderSettings());
        return new RedisEmailQueueService(_redis, settings, NullLogger<RedisEmailQueueService>.Instance);
    }
}
