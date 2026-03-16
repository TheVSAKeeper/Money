using StackExchange.Redis;
using System.Text.Json;

namespace Money.Api.Services.Notifications;

public class AdminNotificationPublisher(IConnectionMultiplexer redis, ILogger<AdminNotificationPublisher> logger)
{
    public async Task PublishEmailQueueChangedAsync(long queueLen, long retryLen, long dlqLen, string trigger)
    {
        try
        {
            var message = JsonSerializer.Serialize(new
            {
                type = "EmailQueueChanged",
                trigger, // "EmailSent" | "EmailFailed" | "EmailRetried" | "EmailDead" | "EmailEnqueued"
                stats = new { queueLen, retryLen, dlqLen },
            });

            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal("admin:email-queue"), message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish admin email-queue notification");
        }
    }

    public async Task PublishCacheFlushedAsync()
    {
        try
        {
            var message = JsonSerializer.Serialize(new { type = "CacheFlushed" });
            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal("admin:system"), message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish admin system notification");
        }
    }
}
