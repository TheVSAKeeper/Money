using Microsoft.AspNetCore.SignalR;
using Money.Api.Hubs;
using StackExchange.Redis;
using System.Threading.Channels;

namespace Money.Api.Services.Notifications;

public class AdminBridgeService(
    IConnectionMultiplexer redis,
    IHubContext<MoneyHub> hub,
    ILogger<AdminBridgeService> logger) : BackgroundService
{
    private readonly Channel<(string Channel, string Message)> _queue = Channel.CreateUnbounded<(string, string)>(new()
    {
        SingleReader = true,
    });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();

        await subscriber.SubscribeAsync(RedisChannel.Pattern("admin:*"), (channel, message) =>
        {
            _queue.Writer.TryWrite((channel.ToString(), message.ToString()));
        });

        await foreach (var (_, message) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await hub.Clients.Group("admin").SendAsync("AdminEvent", message, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to bridge admin notification");
            }
        }
    }
}
