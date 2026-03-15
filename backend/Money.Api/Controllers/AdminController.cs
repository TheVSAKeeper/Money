using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Money.Api.BackgroundServices;
using Money.Api.Dto.Admin;
using Money.Business;
using Money.Data;
using Money.Data.Sharding;
using OpenIddict.Validation.AspNetCore;

namespace Money.Api.Controllers;

/// <summary>
/// Управление и мониторинг системы.
/// </summary>
[ApiController]
[Authorize(
    //   Roles = "Admin",
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("[controller]")]
public class AdminController(
    ShardedDbContextFactory factory,
    RequestEnvironment environment,
    ShardRouter router,
    RoutingDbContext routingDb,
    IMemoryCache cache,
    PartitionMaintenanceService partitionMaintenance,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>
    /// Получить метрики по всем шардам.
    /// </summary>
    /// <returns>Метрики шардов: таблицы, строки, размеры.</returns>
    [HttpGet("Shards")]
    [ProducesResponseType(typeof(ShardsMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ShardsMetricsResponse> GetShardsMetrics()
    {
        logger.LogInformation("Запрос метрик шардов");

        var shards = new Dictionary<string, ShardMetrics>();

        foreach (var shardName in factory.ShardNames)
        {
            await using var dbContext = factory.Create(shardName);
            await using var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();

            command.CommandText = """
                                  SELECT relname, n_live_tup, n_dead_tup, pg_relation_size(relid)
                                  FROM pg_stat_user_tables
                                  ORDER BY n_live_tup DESC;

                                  SELECT pg_database_size(current_database());
                                  """;

            await using var reader = await command.ExecuteReaderAsync();

            var tables = new List<TableMetrics>();
            long totalRows = 0;
            long sizeBytes = 0;

            while (await reader.ReadAsync())
            {
                var liveTup = reader.GetInt64(1);
                var relSize = reader.GetInt64(3);

                tables.Add(new()
                {
                    Name = reader.GetString(0),
                    LiveRows = liveTup,
                    DeadRows = reader.GetInt64(2),
                    SizeBytes = relSize,
                });

                totalRows += liveTup;
                sizeBytes += relSize;
            }

            long dbSizeBytes = 0;

            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                dbSizeBytes = reader.GetInt64(0);
            }

            shards[shardName] = new()
            {
                Tables = tables,
                TotalRows = totalRows,
                SizeBytes = sizeBytes,
                DbSizeBytes = dbSizeBytes,
            };

            logger.LogInformation("Метрики шарда {ShardName}: {TotalRows} строк, размер таблиц={SizeBytes} байт, размер БД={DbSizeBytes} байт",
                shardName,
                totalRows,
                sizeBytes,
                dbSizeBytes);
        }

        var currentShard = environment.ShardName ?? router.ResolveShard(environment.AuthUser!.Id);

        return new()
        {
            Shards = shards,
            CurrentUserShard = currentShard,
        };
    }

    /// <summary>
    /// Получить информацию о партициях таблицы operations по всем шардам.
    /// </summary>
    /// <returns>Список шардов с информацией о партициях и времени последнего обслуживания.</returns>
    [HttpGet("Partitions")]
    [ProducesResponseType(typeof(List<PartitionListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<PartitionListResponse>> GetPartitions()
    {
        const string CacheKey = "admin:partitions";

        if (cache.TryGetValue(CacheKey, out List<PartitionListResponse>? cached))
        {
            return cached!;
        }

        logger.LogInformation("Запрос информации о партициях");

        var result = new List<PartitionListResponse>();

        foreach (var shardName in factory.ShardNames)
        {
            await using var dbContext = factory.Create(shardName);
            await using var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT relname,
                                         pg_total_relation_size(relid) AS size_bytes,
                                         n_live_tup AS approx_rows
                                  FROM pg_stat_user_tables
                                  WHERE relname LIKE 'operations_%'
                                    AND relname != 'operations_default'
                                  ORDER BY relname
                                  """;

            await using var reader = await command.ExecuteReaderAsync();
            var partitions = new List<PartitionInfo>();

            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var sizeBytes = reader.GetInt64(1);
                var approxRows = reader.GetInt64(2);

                var parts = name.Split('_');
                DateOnly rangeStart = default;
                DateOnly rangeEnd = default;

                if (parts.Length == 3
                    && int.TryParse(parts[1], out var year)
                    && int.TryParse(parts[2], out var month))
                {
                    rangeStart = new(year, month, 1);
                    rangeEnd = rangeStart.AddMonths(1);
                }

                partitions.Add(new(name, approxRows, sizeBytes, rangeStart, rangeEnd));
            }

            partitionMaintenance.LastMaintenanceUtc.TryGetValue(shardName, out var lastMaintenance);
            result.Add(new(shardName, lastMaintenance, partitions));
        }

        cache.Set(CacheKey, result, TimeSpan.FromMinutes(5));

        return result;
    }

    /// <summary>
    /// Получить список пользователей с указанием шарда.
    /// </summary>
    [HttpGet("UserShards")]
    [ProducesResponseType(typeof(List<UserShardInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<UserShardInfo>> GetUserShards()
    {
        var mappings = await routingDb.ShardMappings.ToListAsync();

        var authUsers = await routingDb.Users
            .ToDictionaryAsync(u => u.Id, u => (u.UserName ?? "", u.Email ?? ""));

        var domainUserToAuth = new Dictionary<int, Guid>();

        foreach (var shardName in factory.ShardNames)
        {
            await using var db = factory.Create(shardName);
            var domainUsers = await db.DomainUsers.Select(u => new { u.Id, u.AuthUserId }).ToListAsync();

            foreach (var du in domainUsers)
            {
                domainUserToAuth[du.Id] = du.AuthUserId;
            }
        }

        return mappings
            .Select(m =>
            {
                domainUserToAuth.TryGetValue(m.UserId, out var authUserId);
                authUsers.TryGetValue(authUserId, out var user);

                return new UserShardInfo
                {
                    UserName = user.Item1.Length > 0 ? user.Item1 : authUserId.ToString(),
                    Email = user.Item2,
                    ShardName = m.ShardName,
                    AssignedAt = m.AssignedAt,
                };
            })
            .OrderBy(x => x.ShardName)
            .ThenBy(x => x.UserName)
            .ToList();
    }
}
