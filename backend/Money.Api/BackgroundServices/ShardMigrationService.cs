using Microsoft.EntityFrameworkCore;
using Money.Data;
using Money.Data.Sharding;

namespace Money.Api.BackgroundServices;

public sealed class ShardMigrationService(
    IServiceScopeFactory scopeFactory,
    ShardedDbContextFactory factory,
    ILogger<ShardMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var shardNames = factory.ShardNames.ToList();

        logger.LogInformation("Запуск миграций базы данных: RoutingDb + {ShardCount} шардов [{ShardNames}]", shardNames.Count, string.Join(", ", shardNames));

        await MigrateRoutingDb(cancellationToken);

        await Parallel.ForEachAsync(shardNames,
            cancellationToken,
            async (shardName, ct) =>
            {
                await MigrateShard(shardName, ct);
            });

        logger.LogInformation("Все миграции успешно завершены");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task MigrateRoutingDb(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Миграция RoutingDb...");

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RoutingDbContext>();
            await db.Database.MigrateAsync(ct);

            logger.LogInformation("Миграция RoutingDb завершена успешно");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка миграции RoutingDb");
            throw;
        }
    }

    private async Task MigrateShard(string shardName, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Миграция шарда {ShardName}...", shardName);

            await using var db = factory.Create(shardName);
            await db.Database.MigrateAsync(ct);

            logger.LogInformation("Миграция шарда {ShardName} завершена успешно", shardName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка миграции шарда {ShardName}", shardName);
            throw;
        }
    }
}
