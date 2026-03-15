using Microsoft.EntityFrameworkCore;
using Money.Data.Sharding;
using System.Collections.Concurrent;
using System.Data.Common;

namespace Money.Api.BackgroundServices;

public sealed class PartitionMaintenanceService(
    ShardedDbContextFactory factory,
    TimeProvider timeProvider,
    ILogger<PartitionMaintenanceService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastMaintenance = new();

    public IReadOnlyDictionary<string, DateTimeOffset> LastMaintenanceUtc => _lastMaintenance;

    public async Task RunMaintenanceAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Запуск обслуживания партиций таблицы operations");

        await Task.WhenAll(factory.ShardNames.Select(shard => ProcessShardAsync(shard, ct)));

        logger.LogInformation("Обслуживание партиций завершено");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunMaintenanceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24), timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunMaintenanceAsync(stoppingToken);
        }
    }

    private static async Task<HashSet<string>> GetExistingPartitionNamesAsync(
        DbConnection connection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT tablename
                              FROM pg_catalog.pg_tables
                              WHERE tablename LIKE 'operations_%'
                              """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var partitions = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(ct))
        {
            partitions.Add(reader.GetString(0));
        }

        return partitions;
    }

    private async Task ProcessShardAsync(string shardName, CancellationToken ct)
    {
        try
        {
            await using var db = factory.Create(shardName);
            await using var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(ct);

            await using var lockCommand = connection.CreateCommand();
            lockCommand.CommandText = "SELECT pg_try_advisory_lock(hashtext('partition_maintenance')::bigint)";
            var lockAcquired = (bool)(await lockCommand.ExecuteScalarAsync(ct))!;

            if (!lockAcquired)
            {
                logger.LogInformation("Шард {ShardName}: advisory lock уже захвачен другим процессом, пропускаем", shardName);
                return;
            }

            try
            {
                var created = await EnsurePartitionsAsync(shardName, connection, ct);

                if (created.Count > 0)
                {
                    logger.LogInformation("Шард {ShardName}: созданы партиции: {Partitions}", shardName, string.Join(", ", created));
                }
                else
                {
                    logger.LogInformation("Шард {ShardName}: все необходимые партиции уже существуют", shardName);
                }

                _lastMaintenance[shardName] = timeProvider.GetUtcNow();
            }
            finally
            {
                await using var unlockCommand = connection.CreateCommand();
                unlockCommand.CommandText = "SELECT pg_advisory_unlock(hashtext('partition_maintenance')::bigint)";
                await unlockCommand.ExecuteScalarAsync(ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Ошибка обслуживания партиций для шарда {ShardName}", shardName);
        }
    }

    private async Task<List<string>> EnsurePartitionsAsync(
        string shardName,
        DbConnection connection,
        CancellationToken ct)
    {
        var existingPartitions = await GetExistingPartitionNamesAsync(connection, ct);

        var now = timeProvider.GetUtcNow();
        var created = new List<string>();

        for (var offset = 0; offset <= 2; offset++)
        {
            var target = now.AddMonths(offset);
            var partitionName = $"operations_{target.Year}_{target.Month:D2}";

            if (existingPartitions.Contains(partitionName))
            {
                continue;
            }

            var rangeStart = new DateOnly(target.Year, target.Month, 1);
            var rangeEnd = rangeStart.AddMonths(1);

            logger.LogInformation("Шард {ShardName}: создание партиции {PartitionName} [{RangeStart}, {RangeEnd})",
                shardName, partitionName, rangeStart, rangeEnd);

            await using var createCommand = connection.CreateCommand();
            createCommand.CommandText =
                $"""
                 CREATE TABLE IF NOT EXISTS {partitionName}
                     PARTITION OF operations
                     FOR VALUES FROM ('{rangeStart:yyyy-MM-dd}') TO ('{rangeEnd:yyyy-MM-dd}')
                 """;

            await createCommand.ExecuteNonQueryAsync(ct);
            created.Add(partitionName);
        }

        return created;
    }
}
