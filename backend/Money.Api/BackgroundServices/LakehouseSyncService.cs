using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Money.Api.Services.Analytics;
using Money.Api.Services.Lakehouse;
using Money.Data.Entities;
using Money.Data.Sharding;

namespace Money.Api.BackgroundServices;

public sealed class LakehouseSyncService(
    IServiceProvider serviceProvider,
    ShardedDbContextFactory shardFactory,
    OutboxCursorService cursorService,
    IOptions<LakehouseSettings> settings,
    ILogger<LakehouseSyncService> logger) : BackgroundService
{
    public const string ConsumerName = "lakehouse";
    private const int BatchSize = 1000;

    public DateTimeOffset? LastSyncUtc { get; private set; }
    public long TotalEventsProcessed { get; private set; }

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
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(settings.Value.SyncIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunSyncAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Lakehouse sync failed — events remain for next iteration");
            }
        }
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

        logger.LogDebug("Shard {Shard}: writing {Count} events to Lakehouse (cursor: {Cursor})",
            shardName, events.Count, cursor);

        using var scope = serviceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<LakehouseWriter>();

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

        var lastId = events.Max(e => e.Id);
        await cursorService.SetCursorAsync(ConsumerName, shardName, lastId);

        TotalEventsProcessed += events.Count;
    }
}
