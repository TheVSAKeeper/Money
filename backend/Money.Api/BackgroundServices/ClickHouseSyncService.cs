using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Money.Api.Services.Analytics;
using Money.Data.Entities;
using Money.Data.Sharding;
using System.Text.Json;

namespace Money.Api.BackgroundServices;

public sealed class ClickHouseSyncService(
    ClickHouseService clickHouse,
    ShardedDbContextFactory shardFactory,
    IOptions<ClickHouseSettings> settings,
    ILogger<ClickHouseSyncService> logger) : BackgroundService
{
    private const int BatchSize = 1000;

    public DateTimeOffset? LastSyncUtc { get; private set; }

    public async Task RunSyncAsync(CancellationToken ct = default)
    {
        foreach (var shardName in shardFactory.ShardNames)
        {
            await SyncShardAsync(shardName, ct);
        }

        LastSyncUtc = DateTimeOffset.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.Value.SyncIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeRunSyncAsync(stoppingToken);
        }

        await SafeRunSyncAsync(CancellationToken.None);
    }

    private async Task SafeRunSyncAsync(CancellationToken ct)
    {
        try
        {
            await RunSyncAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ClickHouse sync failed — outbox events remain in PostgreSQL for next iteration");
        }
    }

    private async Task SyncShardAsync(string shardName, CancellationToken ct)
    {
        await using var db = shardFactory.Create(shardName);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var events = await db.OutboxEvents
            .FromSqlRaw($"SELECT * FROM outbox_events ORDER BY id LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        if (events.Count == 0)
        {
            return;
        }

        logger.LogDebug("Shard {Shard}: syncing {Count} outbox events to ClickHouse", shardName, events.Count);

        var opEvents = events.Where(e => e.EventType == OutboxEvent.OperationType).ToList();
        var debtEvents = events.Where(e => e.EventType == OutboxEvent.DebtType).ToList();

        if (opEvents.Count > 0)
        {
            var rows = opEvents
                .Select(e => JsonSerializer.Deserialize<OperationPayload>(e.Payload)!)
                .Select(p => new[]
                {
                    p.UserId, p.OperationId, p.CategoryId, p.CategoryName,
                    p.OperationType, p.Sum, p.Date.ToString("yyyy-MM-dd"),
                    p.PlaceName ?? (object)DBNull.Value,
                    p.Comment ?? (object)DBNull.Value,
                });

            await clickHouse.InsertBatchAsync("operations_analytics (user_id, operation_id, category_id, category_name, operation_type, sum, date, place_name, comment)",
                rows,
                ct);
        }

        if (debtEvents.Count > 0)
        {
            var rows = debtEvents
                .Select(e => JsonSerializer.Deserialize<DebtPayload>(e.Payload)!)
                .Select(p => new object[]
                {
                    p.UserId, p.DebtId, p.OwnerName, p.TypeId,
                    p.Sum, p.PaySum, p.StatusId, p.Date.ToString("yyyy-MM-dd"),
                });

            await clickHouse.InsertBatchAsync("debts_analytics (user_id, debt_id, owner_name, type_id, sum, pay_sum, status_id, date)",
                rows,
                ct);
        }

        db.OutboxEvents.RemoveRange(events);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
