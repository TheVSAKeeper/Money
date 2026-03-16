using Microsoft.AspNetCore.SignalR;
using Money.Api.Hubs;
using StackExchange.Redis;
using System.Threading.Channels;

namespace Money.Api.Services.Notifications;

public class NotificationBridgeService(
    IConnectionMultiplexer redis,
    IHubContext<MoneyHub> hub,
    IServiceProvider serviceProvider,
    ILogger<NotificationBridgeService> logger) : BackgroundService
{
    private readonly Channel<(string Channel, string Message)> _queue =
        Channel.CreateUnbounded<(string, string)>(new()
            { SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();

        await subscriber.SubscribeAsync(RedisChannel.Pattern("events:*"), (channel, message) =>
        {
            _queue.Writer.TryWrite((channel.ToString(), message.ToString()));
        });

        await foreach (var (channel, message) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var parts = channel.Split(':');
                var shardName = parts[1];
                var userId = int.Parse(parts[2]);

                await using var scope = serviceProvider.CreateAsyncScope();
                var accountsService = scope.ServiceProvider.GetRequiredService<AccountsService>();
                var authUserId = await accountsService.GetAuthUserIdAsync(userId, shardName, stoppingToken);
                var group = $"user:{authUserId}";

                await hub.Clients.Group(group).SendAsync("Notify", message, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to bridge notification from channel {Channel}", channel);
            }
        }
    }
}
