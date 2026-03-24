using Microsoft.EntityFrameworkCore;
using Money.Api.Services.Analytics;
using Money.Business.Enums;
using Money.Business.Interfaces;
using Money.Data.Graph;
using Money.Data.Sharding;
using StackExchange.Redis;

namespace Money.Api.BackgroundServices;

public class GraphReconciliationService(
    IServiceProvider serviceProvider,
    ShardedDbContextFactory shardFactory,
    Neo4jSchemaInitializer schemaInitializer,
    OutboxCursorService cursorService,
    IConnectionMultiplexer redis,
    IDistributedLockService lockService,
    ILogger<GraphReconciliationService> logger) : BackgroundService
{
    private const string LastSyncKey = "neo4j:reconciliation:lastSync";
    private const string LockKey = "graph:reconciliation";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private static readonly string[] OutboxConsumers =
        [ClickHouseSyncService.ConsumerName, Neo4jSyncService.ConsumerName];

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var lockHandle = await lockService.AcquireAsync(LockKey, TimeSpan.FromMinutes(30), 0, cancellationToken);

            var lastSync = await GetLastSyncTimestampAsync();
            logger.LogInformation("Graph reconciliation started (lastSync: {LastSync})", lastSync);

            foreach (var shardName in shardFactory.ShardNames)
            {
                try
                {
                    await ReconcileShardAsync(shardName, cancellationToken);
                    await CleanupOutboxAsync(shardName, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Reconciliation failed for shard {ShardName}", shardName);
                }
            }

            await SetLastSyncTimestampAsync(DateTime.UtcNow);
            logger.LogInformation("Graph reconciliation completed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Graph reconciliation skipped (lock not acquired or error)");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await schemaInitializer.EnsureSchemaAsync();
            logger.LogInformation("Neo4j schema initialized");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Neo4j schema");
        }

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileAsync(stoppingToken);
        }
    }

    private async Task ReconcileShardAsync(string shardName, CancellationToken cancellationToken)
    {
        await using var dbContext = shardFactory.Create(shardName);
        using var scope = serviceProvider.CreateScope();
        var debtGraph = scope.ServiceProvider.GetRequiredService<IDebtGraphService>();
        var categoryGraph = scope.ServiceProvider.GetRequiredService<ICategoryGraphService>();

        var debts = await dbContext.Debts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(d => d.Owner)
            .ToListAsync(cancellationToken);

        foreach (var debt in debts)
        {
            try
            {
                var status = debt.IsDeleted ? "deleted" : ((DebtStatus)debt.StatusId).ToString().ToLowerInvariant();

                await debtGraph.SyncDebtAsync($"{shardName}_{debt.UserId}", debt.Id, debt.Owner?.Name ?? "Unknown",
                    debt.Sum, status, debt.TypeId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reconcile debt {DebtId} on shard {ShardName}", debt.Id, shardName);
            }
        }

        var categories = await dbContext.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var cat in categories)
        {
            try
            {
                if (cat.IsDeleted)
                {
                    await categoryGraph.DeleteCategoryAsync($"{shardName}_{cat.UserId}", cat.Id);
                }
                else
                {
                    await categoryGraph.SyncCategoryAsync($"{shardName}_{cat.UserId}", cat.Id, cat.Name, cat.TypeId, cat.ParentId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reconcile category {CategoryId} on shard {ShardName}", cat.Id, shardName);
            }
        }

        logger.LogInformation("Reconciled shard {ShardName}: {DebtCount} debts, {CategoryCount} categories",
            shardName, debts.Count, categories.Count);
    }

    private async Task CleanupOutboxAsync(string shardName, CancellationToken cancellationToken)
    {
        try
        {
            var minCursor = await cursorService.GetMinCursorAsync(OutboxConsumers, shardName);

            if (minCursor <= 0)
            {
                return;
            }

            await using var db = shardFactory.Create(shardName);
            var deleted = await db.OutboxEvents
                .Where(e => e.Id <= minCursor)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted > 0)
            {
                logger.LogDebug("Cleaned up {Count} processed outbox events on shard {ShardName}", deleted, shardName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cleanup outbox events on shard {ShardName}", shardName);
        }
    }

    private async Task<DateTime?> GetLastSyncTimestampAsync()
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync(LastSyncKey);
            return value.HasValue ? DateTime.Parse(value!) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task SetLastSyncTimestampAsync(DateTime timestamp)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync(LastSyncKey, timestamp.ToString("O"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save last sync timestamp");
        }
    }
}
