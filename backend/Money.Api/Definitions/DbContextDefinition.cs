using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Money.Api.BackgroundServices;
using Money.Business;
using Money.Data;
using Money.Data.Sharding;

namespace Money.Api.Definitions;

public class DbContextDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.Configure<ShardingOptions>(builder.Configuration.GetSection("Sharding"));

        var shards = builder.Configuration
                         .GetSection("Sharding:Shards")
                         .Get<List<ShardConfig>>()
                     ?? [];

        foreach (var shard in shards)
        {
            builder.AddKeyedNpgsqlDataSource(shard.Name);
        }

        builder.Services.AddSingleton<ShardRouter>();

        builder.Services.AddSingleton<ShardedDbContextFactory>();

        builder.Services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<ShardedDbContextFactory>();
            var env = sp.GetRequiredService<RequestEnvironment>();
            var logger = sp.GetRequiredService<ILogger<DbContextDefinition>>();

            var shardName = env.ShardName
                            ?? throw new InvalidOperationException("ShardName not set. AuthMiddleware must run before resolving ApplicationDbContext.");

            logger.LogDebug("Создание scoped ApplicationDbContext: DomainUserId={UserId}, шард={ShardName}",
                env.TryGetUserId(),
                shardName);

            return factory.Create(shardName);
        });

        builder.AddNpgsqlDbContext<RoutingDbContext>("RoutingDb", settings =>
        {
            settings.DisableHealthChecks = false;
            settings.DisableTracing = false;
            settings.DisableMetrics = false;
            settings.DisableRetry = false;
        }, optionsBuilder =>
        {
            optionsBuilder.UseSnakeCaseNamingConvention();
            optionsBuilder.UseOpenIddict();
            optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        builder.Services.AddHostedService<ShardMigrationService>();
    }
}
