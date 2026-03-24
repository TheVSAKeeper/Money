using Microsoft.EntityFrameworkCore;
using Money.Api.Services.Analytics;
using Money.Business.Enums;
using Money.Data.Entities;
using Money.Data.Graph;
using Money.Data.Sharding;
using System.Text.Json;

namespace Money.Api.BackgroundServices;

public sealed class Neo4jSyncService(
    IServiceProvider serviceProvider,
    ShardedDbContextFactory shardFactory,
    OutboxCursorService cursorService,
    ILogger<Neo4jSyncService> logger) : BackgroundService
{
    public const string ConsumerName = "neo4j";
    private const int BatchSize = 500;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    public DateTimeOffset? LastSyncUtc { get; private set; }

    internal async Task RunSyncAsync(CancellationToken ct = default)
    {
        foreach (var shardName in shardFactory.ShardNames)
        {
            await SyncShardAsync(shardName, ct);
        }

        LastSyncUtc = DateTimeOffset.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunSyncAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Neo4j sync failed — events remain in PostgreSQL for next iteration");
            }
        }
    }

    private static async Task ProcessDebtEventAsync(string shardName, OutboxEvent evt, IDebtGraphService debtGraph)
    {
        var payload = JsonSerializer.Deserialize<DebtPayload>(evt.Payload)!;
        var userId = $"{shardName}_{payload.UserId}";

        if (payload.IsDeleted || payload.Action == "deleted")
        {
            await debtGraph.DeleteDebtAsync(userId, payload.DebtId);
            return;
        }

        var status = Enum.IsDefined(typeof(DebtStatus), payload.StatusId)
            ? ((DebtStatus)payload.StatusId).ToString().ToLowerInvariant()
            : "unknown";

        await debtGraph.SyncDebtAsync(userId, payload.DebtId, payload.OwnerName, payload.Sum, status, payload.TypeId);

        if (payload.PaySum > 0)
        {
            await debtGraph.SyncPaymentAsync(userId, payload.DebtId, payload.PaySum, status);
        }
    }

    private static async Task ProcessCategoryEventAsync(string shardName, OutboxEvent evt, ICategoryGraphService categoryGraph)
    {
        var payload = JsonSerializer.Deserialize<CategoryPayload>(evt.Payload)!;
        var userId = $"{shardName}_{payload.UserId}";

        if (payload.IsDeleted)
        {
            await categoryGraph.DeleteCategoryAsync(userId, payload.CategoryId);
        }
        else
        {
            await categoryGraph.SyncCategoryAsync(userId, payload.CategoryId, payload.Name, payload.TypeId, payload.ParentId);
        }
    }

    private static Task ProcessOperationEventAsync(string shardName, OutboxEvent evt, ICategoryGraphService categoryGraph)
    {
        var payload = JsonSerializer.Deserialize<OperationPayload>(evt.Payload)!;

        if (payload.Action != "added")
        {
            return Task.CompletedTask;
        }

        var userId = $"{shardName}_{payload.UserId}";
        var yearMonth = payload.Date.ToString("yyyy-MM");
        var amountCents = (long)(payload.Sum * 100);

        return categoryGraph.UpdateOperationFlowAsync(userId, payload.CategoryId, yearMonth, amountCents, 1);
    }

    private async Task SyncShardAsync(string shardName, CancellationToken ct)
    {
        var cursor = await cursorService.GetCursorAsync(ConsumerName, shardName);

        await using var db = shardFactory.Create(shardName);

        var events = await db.OutboxEvents
            .Where(e => e.Id > cursor)
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (events.Count == 0)
        {
            return;
        }

        logger.LogDebug("Shard {Shard}: syncing {Count} outbox events to Neo4j (cursor: {Cursor})",
            shardName, events.Count, cursor);

        using var scope = serviceProvider.CreateScope();
        var debtGraph = scope.ServiceProvider.GetRequiredService<IDebtGraphService>();
        var categoryGraph = scope.ServiceProvider.GetRequiredService<ICategoryGraphService>();

        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                switch (evt.EventType)
                {
                    case OutboxEvent.DebtType:
                        await ProcessDebtEventAsync(shardName, evt, debtGraph);
                        break;

                    case OutboxEvent.CategoryType:
                        await ProcessCategoryEventAsync(shardName, evt, categoryGraph);
                        break;

                    case OutboxEvent.OperationType:
                        await ProcessOperationEventAsync(shardName, evt, categoryGraph);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process outbox event {EventId} ({EventType}) for Neo4j",
                    evt.Id, evt.EventType);
            }
        }

        var lastId = events.Max(e => e.Id);
        await cursorService.SetCursorAsync(ConsumerName, shardName, lastId);
    }
}
