using Money.Business.Interfaces;
using StackExchange.Redis;
using System.Text.Json;

namespace Money.Api.Services.Notifications;

public class RedisNotificationService(
    IConnectionMultiplexer redis,
    ILogger<RedisNotificationService> logger) : INotificationService
{
    public async Task PublishAsync(int userId, string shardName, string eventType, object payload)
    {
        try
        {
            var channel = RedisChannel.Literal($"events:{shardName}:{userId}");
            var message = JsonSerializer.Serialize(new { type = eventType, data = payload });
            await redis.GetSubscriber().PublishAsync(channel, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish notification {EventType} for user {ShardName}:{UserId}",
                eventType, shardName, userId);
        }
    }
}
