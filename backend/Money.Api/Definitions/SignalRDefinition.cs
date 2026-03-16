using Money.Api.Hubs;
using StackExchange.Redis;

namespace Money.Api.Definitions;

public class SignalRDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        var signalR = builder.Services.AddSignalR();
        var redisConn = builder.Configuration.GetConnectionString("redis");

        if (redisConn != null)
        {
            signalR.AddStackExchangeRedis(redisConn, opts =>
            {
                opts.Configuration.ChannelPrefix = RedisChannel.Literal("money");
            });
        }
    }

    public override void ConfigureApplication(WebApplication app)
    {
        app.MapHub<MoneyHub>("/hubs/money");
    }
}
