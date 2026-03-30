using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Money.Api.Services.Analytics;
using Money.Api.Services.Lakehouse;
using Money.Business.Interfaces;
using Money.Data.Entities;
using Money.Data.Sharding;

namespace Money.Api.BackgroundServices;

public sealed class LakehouseReconciliationService(
    IServiceProvider serviceProvider,
    ShardedDbContextFactory shardFactory,
    OutboxCursorService cursorService,
    IDistributedLockService lockService,
    IOptions<LakehouseSettings> settings,
    ILogger<LakehouseReconciliationService> logger) : BackgroundService
{
    private const string LockKey = "lakehouse:reconciliation";
    private const int BatchSize = 5000;

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var lockHandle = await lockService.AcquireAsync(LockKey, TimeSpan.FromMinutes(10), 0, cancellationToken);

            logger.LogInformation("Lakehouse reconciliation started");

            foreach (var shardName in shardFactory.ShardNames)
            {
                try
                {
                    await ReconcileShardAsync(shardName, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Lakehouse reconciliation failed for shard {ShardName}", shardName);
                }
            }

            logger.LogInformation("Lakehouse reconciliation completed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Lakehouse reconciliation skipped (lock not acquired or error)");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(settings.Value.ReconciliationIntervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileAsync(stoppingToken);
        }
    }

    private async Task ReconcileShardAsync(string shardName, CancellationToken ct)
    {
        var cursor = await cursorService.GetCursorAsync(LakehouseSyncService.ConsumerName, shardName);
        long totalReconciled = 0;
        long lastProcessedId = 0;

        while (true)
        {
            await using var db = shardFactory.Create(shardName);

            var events = await db.OutboxEvents
                .AsNoTracking()
                .Where(e => e.Id <= cursor && e.Id > lastProcessedId)
                .OrderBy(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (events.Count == 0)
            {
                break;
            }

            using var scope = serviceProvider.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<LakehouseWriter>();

            await WriteBatchByType(writer, shardName, events, ct);

            lastProcessedId = events[^1].Id;
            totalReconciled += events.Count;

            logger.LogDebug("Lakehouse reconciliation progress for shard {ShardName}: {Processed} events",
                shardName, totalReconciled);

            if (events.Count < BatchSize)
            {
                break;
            }
        }

        if (totalReconciled > 0)
        {
            logger.LogInformation("Lakehouse reconciliation completed for shard {ShardName}: {Count} events",
                shardName, totalReconciled);
        }
    }

    private static async Task WriteBatchByType(
        LakehouseWriter writer, string shardName, List<OutboxEvent> events, CancellationToken ct)
    {
        var opEvents = events.Where(e => e.EventType == OutboxEvent.OperationType).ToList();
        var debtEvents = events.Where(e => e.EventType == OutboxEvent.DebtType).ToList();
        var catEvents = events.Where(e => e.EventType == OutboxEvent.CategoryType).ToList();

        if (opEvents.Count > 0)
        {
            await writer.WriteBronzeAsync(shardName, "operations", opEvents, ct);
        }

        if (debtEvents.Count > 0)
        {
            await writer.WriteBronzeAsync(shardName, "debts", debtEvents, ct);
        }

        if (catEvents.Count > 0)
        {
            await writer.WriteBronzeAsync(shardName, "categories", catEvents, ct);
        }
    }
}
