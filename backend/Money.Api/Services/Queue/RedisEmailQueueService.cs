using Microsoft.Extensions.Options;
using Money.Api.BackgroundServices;
using Money.Business.Interfaces;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Money.Api.Services.Queue;

public class RedisEmailQueueService(
    IConnectionMultiplexer redis,
    IOptions<EmailSenderSettings> options,
    ILogger<RedisEmailQueueService> logger) : IEmailQueueService
{
    private const string QueueKey = "email:queue";
    private const string RetryKey = "email:retry";
    private const string DlqKey = "email:dlq";

    private const string DequeueRetriesScript =
        """
        local items = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1], 'LIMIT', 0, ARGV[2])
        if #items > 0 then
            redis.call('ZREM', KEYS[1], unpack(items))
        end
        return items
        """;

    private readonly EmailSenderSettings _settings = options.Value;
    private readonly ConcurrentQueue<MailEnvelope> _fallback = new();

    public async Task EnqueueAsync(MailMessage message)
    {
        var envelope = new MailEnvelope { Message = message };

        try
        {
            var db = redis.GetDatabase();
            var json = JsonSerializer.Serialize(envelope);
            await db.ListLeftPushAsync(QueueKey, json);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при постановке в очередь сообщения {MessageId}, используется fallback-буфер", message.Id);
            _fallback.Enqueue(envelope);
        }
    }

    public async Task<List<MailEnvelope>> DequeueBatchAsync(int count)
    {
        var result = new List<MailEnvelope>();

        while (result.Count < count && _fallback.TryDequeue(out var fallbackItem))
        {
            result.Add(fallbackItem);
        }

        if (result.Count >= count)
        {
            return result;
        }

        try
        {
            var db = redis.GetDatabase();
            var remaining = count - result.Count;
            var values = await db.ListRightPopAsync(QueueKey, remaining) ?? [];

            foreach (var value in values)
            {
                if (value.IsNullOrEmpty)
                {
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<MailEnvelope>((string)value!);

                if (envelope != null)
                {
                    result.Add(envelope);
                }
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при извлечении пакета из очереди");
        }

        return result;
    }

    public async Task EnqueueRetryAsync(MailEnvelope envelope)
    {
        var delay = TimeSpan.FromSeconds(_settings.RetryBaseDelaySeconds * Math.Pow(2, envelope.RetryCount));
        envelope.NextRetryAt = DateTimeOffset.UtcNow.Add(delay);

        try
        {
            var db = redis.GetDatabase();
            var json = JsonSerializer.Serialize(envelope);
            var score = envelope.NextRetryAt.Value.ToUnixTimeMilliseconds();
            await db.SortedSetAddAsync(RetryKey, json, score);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при постановке в retry-очередь сообщения {MessageId}", envelope.Message.Id);
        }
    }

    public async Task EnqueueDeadLetterAsync(MailEnvelope envelope)
    {
        try
        {
            var db = redis.GetDatabase();
            var json = JsonSerializer.Serialize(envelope);
            await db.ListLeftPushAsync(DlqKey, json);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при постановке в DLQ сообщения {MessageId}", envelope.Message.Id);
        }
    }

    public async Task<List<MailEnvelope>> DequeueReadyRetriesAsync(int count)
    {
        var result = new List<MailEnvelope>();

        try
        {
            var db = redis.GetDatabase();
            var nowScore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var values = (RedisValue[])(await db.ScriptEvaluateAsync(DequeueRetriesScript, [new(RetryKey)], [nowScore, count]))!;

            foreach (var value in values)
            {
                if (value.IsNullOrEmpty)
                {
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<MailEnvelope>((string)value!);

                if (envelope != null)
                {
                    result.Add(envelope);
                }
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при извлечении готовых retry-сообщений");
        }

        return result;
    }

    public async Task<long> GetQueueLengthAsync()
    {
        try
        {
            var db = redis.GetDatabase();
            return await db.ListLengthAsync(QueueKey);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при получении длины очереди");
            return -1;
        }
    }

    public async Task<long> GetRetryQueueLengthAsync()
    {
        try
        {
            var db = redis.GetDatabase();
            return await db.SortedSetLengthAsync(RetryKey);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при получении длины retry-очереди");
            return -1;
        }
    }

    public async Task<long> GetDeadLetterQueueLengthAsync()
    {
        try
        {
            var db = redis.GetDatabase();
            return await db.ListLengthAsync(DlqKey);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при получении длины DLQ");
            return -1;
        }
    }

    public async Task<List<MailEnvelope>> PeekQueueAsync(int count)
    {
        var result = new List<MailEnvelope>();

        try
        {
            var db = redis.GetDatabase();
            var values = await db.ListRangeAsync(QueueKey, -count);

            foreach (var value in values)
            {
                if (value.IsNullOrEmpty)
                {
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<MailEnvelope>((string)value!);

                if (envelope != null)
                {
                    result.Add(envelope);
                }
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при просмотре очереди");
        }

        return result;
    }

    public async Task<List<MailEnvelope>> PeekRetryQueueAsync(int count)
    {
        var result = new List<MailEnvelope>();

        try
        {
            var db = redis.GetDatabase();
            var values = await db.SortedSetRangeByRankAsync(RetryKey, 0, count - 1);

            foreach (var value in values)
            {
                if (value.IsNullOrEmpty)
                {
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<MailEnvelope>((string)value!);

                if (envelope != null)
                {
                    result.Add(envelope);
                }
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при просмотре retry-очереди");
        }

        return result;
    }

    public async Task<List<MailEnvelope>> PeekDeadLetterQueueAsync(int count)
    {
        var result = new List<MailEnvelope>();

        try
        {
            var db = redis.GetDatabase();
            var values = await db.ListRangeAsync(DlqKey, 0, count - 1);

            foreach (var value in values)
            {
                if (value.IsNullOrEmpty)
                {
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<MailEnvelope>((string)value!);

                if (envelope != null)
                {
                    result.Add(envelope);
                }
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis недоступен при просмотре DLQ");
        }

        return result;
    }
}
