using Microsoft.EntityFrameworkCore;
using Money.Api.Services.Analytics;
using Money.Business.Interfaces;
using Money.Data;
using Money.Data.Sharding;

namespace Money.Api.BackgroundServices;

public sealed class ClickHouseReconciliationService(
    ClickHouseService clickHouse,
    ShardedDbContextFactory shardFactory,
    IDistributedLockService lockService,
    ILogger<ClickHouseReconciliationService> logger) : BackgroundService
{
    private const string LockKey = "clickhouse:reconciliation";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var lockHandle = await lockService.AcquireAsync(LockKey, TimeSpan.FromMinutes(30), 0, cancellationToken);

            logger.LogInformation("ClickHouse reconciliation started");

            foreach (var shardName in shardFactory.ShardNames)
            {
                try
                {
                    await ReconcileShardAsync(shardName, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "ClickHouse reconciliation failed for shard {ShardName}", shardName);
                }
            }

            logger.LogInformation("ClickHouse reconciliation completed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ClickHouse reconciliation skipped (lock not acquired or error)");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileAsync(stoppingToken);
        }
    }

    private async Task ReconcileShardAsync(string shardName, CancellationToken ct)
    {
        await using var db = shardFactory.Create(shardName);

        await ReconcileOperationsAsync(db, ct);
        await ReconcileDebtsAsync(db, ct);

        logger.LogInformation("ClickHouse reconciliation completed for shard {ShardName}", shardName);
    }

    private async Task ReconcileOperationsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var operations = await db.Operations
            .IgnoreQueryFilters()
            .Where(o => !o.IsDeleted)
            .AsNoTracking()
            .Include(o => o.Category)
            .ToListAsync(ct);

        if (operations.Count == 0)
        {
            return;
        }

        var places = await db.Places
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id, ct);

        var rows = operations.Select(op =>
        {
            string? placeName = null;

            if (op.PlaceId.HasValue && places.TryGetValue(op.PlaceId.Value, out var place))
            {
                placeName = place.Name;
            }

            return new[]
            {
                op.UserId, op.Id, op.CategoryId, op.Category?.Name ?? (object)"",
                op.Category?.TypeId ?? 0, op.Sum, DateOnly.FromDateTime(op.Date).ToString("yyyy-MM-dd"),
                placeName ?? (object)DBNull.Value,
                op.Comment ?? (object)DBNull.Value,
            };
        });

        await clickHouse.InsertBatchAsync("operations_analytics (user_id, operation_id, category_id, category_name, operation_type, sum, date, place_name, comment)",
            rows,
            ct);

        logger.LogDebug("Reconciled {Count} operations to ClickHouse", operations.Count);
    }

    private async Task ReconcileDebtsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var debts = await db.Debts
            .IgnoreQueryFilters()
            .Where(d => !d.IsDeleted)
            .AsNoTracking()
            .Include(d => d.Owner)
            .ToListAsync(ct);

        if (debts.Count == 0)
        {
            return;
        }

        var rows = debts.Select(d => new object[]
        {
            d.UserId, d.Id, d.Owner?.Name ?? "Unknown", d.TypeId,
            d.Sum, d.PaySum, d.StatusId, DateOnly.FromDateTime(d.Date).ToString("yyyy-MM-dd"),
        });

        await clickHouse.InsertBatchAsync("debts_analytics (user_id, debt_id, owner_name, type_id, sum, pay_sum, status_id, date)",
            rows,
            ct);

        logger.LogDebug("Reconciled {Count} debts to ClickHouse", debts.Count);
    }
}
