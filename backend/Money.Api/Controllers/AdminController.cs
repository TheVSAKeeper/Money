using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Money.Api.BackgroundServices;
using Money.Api.Dto.Admin;
using Money.Api.Services.Analytics;
using Money.Api.Services.Lakehouse;
using Money.Api.Services.Notifications;
using Money.Business;
using Money.Business.Interfaces;
using Money.Data;
using Money.Data.Graph;
using Money.Data.Sharding;
using OpenIddict.Validation.AspNetCore;
using StackExchange.Redis;

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
    IConnectionMultiplexer redis,
    IEmailQueueService emailQueueService,
    AdminNotificationPublisher adminPublisher,
    ClickHouseService clickHouseService,
    ClickHouseSyncService clickHouseSyncService,
    LakehouseSyncService lakehouseSyncService,
    LakehouseQueryService lakehouseQueryService,
    LakehouseTransformService lakehouseTransformService,
    LakehouseReconciliationService lakehouseReconciliationService,
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

    /// <summary>
    /// Получить статистику кэша Redis (hits, misses, memory, keys).
    /// </summary>
    [HttpGet("cache/stats")]
    [ProducesResponseType(typeof(CacheStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<CacheStatsResponse> GetCacheStats()
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);

        var info = await server.InfoAsync();
        var totalKeys = await server.DatabaseSizeAsync();

        long hitsTotal = 0;
        long missesTotal = 0;
        long usedMemoryBytes = 0;
        var usedMemoryHuman = "";

        foreach (var section in info)
        {
            foreach (var entry in section)
            {
                switch (entry.Key)
                {
                    case "keyspace_hits":
                        hitsTotal = long.Parse(entry.Value);
                        break;

                    case "keyspace_misses":
                        missesTotal = long.Parse(entry.Value);
                        break;

                    case "used_memory":
                        usedMemoryBytes = long.Parse(entry.Value);
                        break;

                    case "used_memory_human":
                        usedMemoryHuman = entry.Value;
                        break;
                }
            }
        }

        var totalRequests = hitsTotal + missesTotal;

        return new()
        {
            TotalKeys = totalKeys,
            HitsTotal = hitsTotal,
            MissesTotal = missesTotal,
            HitRatio = totalRequests > 0 ? (double)hitsTotal / totalRequests : 0,
            UsedMemoryBytes = usedMemoryBytes,
            UsedMemoryHuman = usedMemoryHuman,
        };
    }

    /// <summary>
    /// Получить список кэшированных категорий с TTL.
    /// </summary>
    [HttpGet("cache/categories")]
    [ProducesResponseType(typeof(List<CacheCategoryInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<CacheCategoryInfo>> GetCachedCategories()
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var db = redis.GetDatabase();
        var result = new List<CacheCategoryInfo>();

        await foreach (var key in server.KeysAsync(pattern: "cache:categories:*"))
        {
            var keyStr = (string)key!;
            // cache:categories:{shardName}:{userId}
            var parts = keyStr.Split(':');

            if (parts.Length != 4 || !int.TryParse(parts[3], out var userId))
            {
                continue;
            }

            var ttl = await db.KeyTimeToLiveAsync(key);

            result.Add(new()
            {
                Key = keyStr,
                ShardName = parts[2],
                UserId = userId,
                TtlRemainingSeconds = ttl?.TotalSeconds,
            });
        }

        return result.OrderBy(x => x.ShardName).ThenBy(x => x.UserId).ToList();
    }

    /// <summary>
    /// Получить информацию о кэшированных операциях (индексы по пользователям).
    /// </summary>
    [HttpGet("cache/operations")]
    [ProducesResponseType(typeof(List<CacheOperationIndexInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<CacheOperationIndexInfo>> GetCachedOperations()
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var db = redis.GetDatabase();
        var result = new List<CacheOperationIndexInfo>();

        await foreach (var key in server.KeysAsync(pattern: "cache:operations:index:*"))
        {
            var keyStr = (string)key!;
            // cache:operations:index:{shardName}:{userId}
            var parts = keyStr.Split(':');

            if (parts.Length != 5 || !int.TryParse(parts[4], out var userId))
            {
                continue;
            }

            var members = await db.SetMembersAsync(key);
            double totalTtl = 0;
            var ttlCount = 0;

            foreach (var member in members)
            {
                var memberTtl = await db.KeyTimeToLiveAsync((string)member!);

                if (!memberTtl.HasValue)
                {
                    continue;
                }

                totalTtl += memberTtl.Value.TotalSeconds;
                ttlCount++;
            }

            result.Add(new()
            {
                ShardName = parts[3],
                UserId = userId,
                CachedFilterCount = members.Length,
                AvgTtlSeconds = ttlCount > 0 ? totalTtl / ttlCount : null,
            });
        }

        return result.OrderBy(x => x.ShardName).ThenBy(x => x.UserId).ToList();
    }

    /// <summary>
    /// Получить текущие значения Redis-счётчиков.
    /// </summary>
    [HttpGet("counters")]
    [ProducesResponseType(typeof(List<CacheCounterInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<List<CacheCounterInfo>> GetCounters()
    {
        var db = redis.GetDatabase();
        var counterKeys = await db.SetMembersAsync("counter:index");
        var result = new List<CacheCounterInfo>();

        foreach (var redisKey in counterKeys)
        {
            var key = (string)redisKey!;
            // counter:{entityType}:{shardName}:{userId}
            var parts = key.Split(':');

            if (parts.Length != 4)
            {
                continue;
            }

            if (!int.TryParse(parts[3], out var userId))
            {
                continue;
            }

            var value = (long?)await db.StringGetAsync(key) ?? 0;

            result.Add(new()
            {
                Key = key,
                EntityType = parts[1],
                ShardName = parts[2],
                UserId = userId,
                CurrentValue = value,
            });
        }

        return result.OrderBy(x => x.ShardName).ThenBy(x => x.UserId).ThenBy(x => x.EntityType).ToList();
    }

    /// <summary>
    /// Получить статистику распределённых блокировок (acquired/failed).
    /// </summary>
    [HttpGet("locks/stats")]
    [ProducesResponseType(typeof(LockStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<LockStatsResponse> GetLockStats()
    {
        var db = redis.GetDatabase();

        var acquiredValue = await db.StringGetAsync("lock:stats:acquired");
        var failedValue = await db.StringGetAsync("lock:stats:failed");

        return new()
        {
            Acquired = acquiredValue.HasValue ? (long)acquiredValue : 0,
            Failed = failedValue.HasValue ? (long)failedValue : 0,
        };
    }

    /// <summary>
    /// Получить метрики Redis Pub/Sub каналов.
    /// </summary>
    [HttpGet("PubSub")]
    [ProducesResponseType(typeof(PubSubMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<PubSubMetricsResponse> GetPubSubMetrics()
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);

        var patternCount = await server.SubscriptionPatternCountAsync();

        return new()
        {
            PatternSubscribers = patternCount,
        };
    }

    /// <summary>
    /// Получить статистику очереди email.
    /// </summary>
    [HttpGet("EmailQueue")]
    [ProducesResponseType(typeof(EmailQueueStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<EmailQueueStatsResponse> GetEmailQueueStats()
    {
        var (queueLength, retryLength, dlqLength, recent, retryItems, dlqItems) = (
            await emailQueueService.GetQueueLengthAsync(),
            await emailQueueService.GetRetryQueueLengthAsync(),
            await emailQueueService.GetDeadLetterQueueLengthAsync(),
            await emailQueueService.PeekQueueAsync(10),
            await emailQueueService.PeekRetryQueueAsync(10),
            await emailQueueService.PeekDeadLetterQueueAsync(10));

        return new()
        {
            QueueLength = queueLength,
            RetryLength = retryLength,
            DlqLength = dlqLength,
            RecentMessages = recent.Select(ToPreview).ToList(),
            RetryMessages = retryItems.Select(ToPreview).ToList(),
            DlqMessages = dlqItems.Select(ToPreview).ToList(),
        };

        static EmailPreview ToPreview(MailEnvelope e)
        {
            return new()
            {
                Id = e.Message.Id,
                ReceiverEmail = e.Message.ReceiverEmail,
                Title = e.Message.Title,
                RetryCount = e.RetryCount,
                EnqueuedAt = e.EnqueuedAt,
                NextRetryAt = e.NextRetryAt,
            };
        }
    }

    /// <summary>
    /// Добавить демо-сообщения в очередь email (для демонстрации панели мониторинга).
    /// </summary>
    [HttpPost("EmailQueue/simulate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SimulateEmailQueue()
    {
        var users = new[]
        {
            "ivan.petrov", "anna.smirnova", "dmitry.kozlov", "elena.novikova", "sergey.popov",
        };

        var subjects = new[]
        {
            "Подтверждение регистрации", "Сброс пароля", "Уведомление о входе",
            "Подтверждение email", "Добро пожаловать!",
        };

        foreach (var (user, subject) in users.Zip(subjects))
        {
            await emailQueueService.EnqueueAsync(new MailMessage($"{user}@example.com",
                subject,
                $"Здравствуйте, {user}! {subject}."));
        }

        await emailQueueService.EnqueueRetryAsync(new MailEnvelope
        {
            Message = new("retry.user@example.com", "Повторная попытка", "Письмо ожидает повторной отправки."),
            RetryCount = 1,
        });

        await emailQueueService.EnqueueDeadLetterAsync(new MailEnvelope
        {
            Message = new("dead.letter@example.com", "Недоставленное письмо", "Превышен лимит попыток."),
            RetryCount = 3,
        });

        logger.LogInformation("Демо-данные добавлены в email-очередь пользователем {UserId}", environment.UserId);
        return NoContent();
    }

    /// <summary>
    /// Сбросить весь кэш Redis (только для Admin).
    /// </summary>
    [HttpDelete("cache/flush")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FlushCache()
    {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        await server.FlushDatabaseAsync();
        await adminPublisher.PublishCacheFlushedAsync();
        logger.LogWarning("Кэш Redis сброшен пользователем {UserId}", environment.UserId);
        return NoContent();
    }

    /// <summary>
    /// Статистика таблиц ClickHouse: строки, размер, партиции.
    /// </summary>
    [HttpGet("ClickHouse/Stats")]
    [ProducesResponseType(typeof(ClickHouseStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ClickHouseStatsResponse> GetClickHouseStats()
    {
        var tables = await clickHouseService.QueryAsync("""
                                                        SELECT
                                                            name,
                                                            total_rows,
                                                            total_bytes,
                                                            (
                                                                SELECT count()
                                                                FROM system.parts
                                                                WHERE active AND database = 'default' AND table = t.name
                                                            ) AS partition_count
                                                        FROM system.tables t
                                                        WHERE name IN ('operations_analytics', 'debts_analytics', 'api_metrics') and total_bytes != 0
                                                        """,
            r => new ClickHouseTableInfo
            {
                Name = r.GetString(0),
                Rows = r.IsDBNull(1) ? 0 : (long)(ulong)r.GetValue(1),
                Bytes = r.IsDBNull(2) ? 0 : (long)(ulong)r.GetValue(2),
                Partitions = r.IsDBNull(3) ? 0 : (int)(ulong)r.GetValue(3),
            });

        return new()
        {
            Tables = tables,
            LastSyncUtc = clickHouseSyncService.LastSyncUtc,
        };
    }

    /// <summary>
    /// API запросов в минуту за последние 30 минут (для графика).
    /// </summary>
    [HttpGet("ClickHouse/ApiMetrics")]
    [ProducesResponseType(typeof(List<ApiMetricsPerMinute>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<List<ApiMetricsPerMinute>> GetApiMetricsPerMinute()
    {
        return clickHouseService.QueryAsync("""
                                            SELECT
                                                toStartOfMinute(timestamp) AS minute,
                                                count() AS requests
                                            FROM api_metrics
                                            WHERE timestamp > now() - INTERVAL 30 MINUTE
                                            GROUP BY minute
                                            ORDER BY minute
                                            """,
            r => new ApiMetricsPerMinute
            {
                Minute = r.GetDateTime(0),
                Requests = r.IsDBNull(1) ? 0 : (long)(ulong)r.GetValue(1),
            });
    }

    /// <summary>
    /// Top-10 самых медленных endpoint'ов.
    /// </summary>
    [HttpGet("ClickHouse/SlowEndpoints")]
    [ProducesResponseType(typeof(List<SlowEndpointInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<List<SlowEndpointInfo>> GetSlowEndpoints()
    {
        return clickHouseService.QueryAsync("""
                                            SELECT
                                                endpoint,
                                                avg(duration_ms),
                                                quantile(0.95)(duration_ms),
                                                countIf(status_code >= 400) * 100.0 / count()
                                            FROM api_metrics
                                            WHERE timestamp > now() - INTERVAL 24 HOUR
                                            GROUP BY endpoint
                                            ORDER BY avg(duration_ms) DESC
                                            LIMIT 10
                                            """,
            r => new SlowEndpointInfo
            {
                Endpoint = r.GetString(0),
                AvgDurationMs = r.GetDouble(1),
                P95DurationMs = r.GetDouble(2),
                ErrorRatePercent = r.GetDouble(3),
            });
    }

    /// <summary>
    /// Получить граф долговых отношений из Neo4j.
    /// </summary>
    [HttpGet("neo4j/debt-graph")]
    [ProducesResponseType(typeof(GraphDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<GraphDto> GetDebtGraph(
        [FromServices] IDebtGraphService debtGraph,
        [FromQuery] int limit = 200)
    {
        limit = Math.Clamp(limit, 1, 1000);
        return await debtGraph.GetDebtNetworkAsync($"{environment.ShardName}_{environment.UserId}", limit);
    }

    /// <summary>
    /// Получить дерево категорий из Neo4j.
    /// </summary>
    [HttpGet("neo4j/categories")]
    [ProducesResponseType(typeof(GraphDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<GraphDto> GetCategoryTree(
        [FromServices] ICategoryGraphService categoryGraph,
        [FromQuery] int limit = 500)
    {
        limit = Math.Clamp(limit, 1, 2000);
        return await categoryGraph.GetCategoryTreeAsync($"{environment.ShardName}_{environment.UserId}", limit);
    }

    /// <summary>
    /// Статистика Data Lakehouse: слои, файлы, размеры.
    /// </summary>
    [HttpGet("Lakehouse/Stats")]
    [ProducesResponseType(typeof(LakehouseStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<LakehouseStatsResponse> GetLakehouseStats()
    {
        var storageInfo = await lakehouseQueryService.GetStorageInfoAsync(HttpContext.RequestAborted);

        return new()
        {
            Layers = storageInfo.Layers,
            LastSyncUtc = lakehouseSyncService.LastSyncUtc,
            TotalEventsProcessed = lakehouseSyncService.TotalEventsProcessed,
        };
    }

    /// <summary>
    /// Запрос к Lakehouse через DuckDB (Bronze/Silver/Gold Parquet).
    /// </summary>
    [HttpPost("Lakehouse/QueryDuckDb")]
    [ProducesResponseType(typeof(List<Dictionary<string, object?>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<List<Dictionary<string, object?>>> QueryLakehouseDuckDb([FromBody] LakehouseQueryRequest request)
    {
        return lakehouseQueryService.QueryDuckDbAsync(request.Sql, HttpContext.RequestAborted);
    }

    /// <summary>
    /// Запрос к Lakehouse через Trino (федеративный SQL).
    /// </summary>
    [HttpPost("Lakehouse/QueryTrino")]
    [ProducesResponseType(typeof(List<Dictionary<string, object?>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<List<Dictionary<string, object?>>> QueryLakehouseTrino([FromBody] LakehouseQueryRequest request)
    {
        return lakehouseQueryService.QueryTrinoAsync(request.Sql, HttpContext.RequestAborted);
    }

    /// <summary>
    /// Принудительный запуск синхронизации Lakehouse.
    /// </summary>
    [HttpPost("Lakehouse/Sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForceLakehouseSync()
    {
        await lakehouseSyncService.RunSyncAsync(HttpContext.RequestAborted);
        logger.LogInformation("Lakehouse sync forced by user {UserId}", environment.UserId);
        return NoContent();
    }

    /// <summary>
    /// Принудительный запуск трансформации Bronze → Silver → Gold.
    /// </summary>
    [HttpPost("Lakehouse/Transform")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForceLakehouseTransform()
    {
        await lakehouseTransformService.TransformBronzeToSilverAsync(HttpContext.RequestAborted);
        await lakehouseTransformService.TransformSilverToGoldAsync(HttpContext.RequestAborted);
        logger.LogInformation("Lakehouse transform forced by user {UserId}", environment.UserId);
        return NoContent();
    }

    /// <summary>
    /// Принудительный запуск сверки Lakehouse.
    /// </summary>
    [HttpPost("Lakehouse/Reconcile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForceLakehouseReconciliation()
    {
        await lakehouseReconciliationService.ReconcileAsync(HttpContext.RequestAborted);
        logger.LogInformation("Lakehouse reconciliation forced by user {UserId}", environment.UserId);
        return NoContent();
    }

    public class LakehouseQueryRequest
    {
        public string Sql { get; set; } = "";
    }
}
