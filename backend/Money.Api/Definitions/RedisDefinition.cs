namespace Money.Api.Definitions;

public class RedisDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.AddRedisClient("redis", configureOptions: opts => opts.AllowAdmin = true);
    }
}
