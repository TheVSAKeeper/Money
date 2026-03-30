namespace Money.ApiClient;

public sealed class AdminClient(MoneyClient apiClient) : ApiClientExecutor(apiClient)
{
    private const string BaseUri = "/Admin";

    protected override string ApiPrefix => "";

    public Task<ApiClientResponse<ShardsMetricsResponse>> GetShardsMetrics()
    {
        return GetAsync<ShardsMetricsResponse>($"{BaseUri}/Shards");
    }

    public Task<ApiClientResponse<List<UserShardInfo>>> GetUserShards()
    {
        return GetAsync<List<UserShardInfo>>($"{BaseUri}/UserShards");
    }

    public Task<ApiClientResponse<List<PartitionListResponse>>> GetPartitions()
    {
        return GetAsync<List<PartitionListResponse>>($"{BaseUri}/Partitions");
    }

    public Task<ApiClientResponse<CacheStatsResponse>> GetCacheStats()
    {
        return GetAsync<CacheStatsResponse>($"{BaseUri}/cache/stats");
    }

    public Task<ApiClientResponse<List<CacheCategoryInfo>>> GetCachedCategories()
    {
        return GetAsync<List<CacheCategoryInfo>>($"{BaseUri}/cache/categories");
    }

    public Task<ApiClientResponse<List<CacheOperationIndexInfo>>> GetCachedOperations()
    {
        return GetAsync<List<CacheOperationIndexInfo>>($"{BaseUri}/cache/operations");
    }

    public Task<ApiClientResponse<List<CacheCounterInfo>>> GetCounters()
    {
        return GetAsync<List<CacheCounterInfo>>($"{BaseUri}/counters");
    }

    public Task<ApiClientResponse<LockStatsResponse>> GetLockStats()
    {
        return GetAsync<LockStatsResponse>($"{BaseUri}/locks/stats");
    }

    public Task<ApiClientResponse> FlushCache()
    {
        return DeleteAsync($"{BaseUri}/cache/flush");
    }

    public Task<ApiClientResponse<PubSubMetricsResponse>> GetPubSubMetrics()
    {
        return GetAsync<PubSubMetricsResponse>($"{BaseUri}/PubSub");
    }

    public Task<ApiClientResponse<EmailQueueStatsResponse>> GetEmailQueueStatsAsync()
    {
        return GetAsync<EmailQueueStatsResponse>($"{BaseUri}/EmailQueue");
    }

    public Task<ApiClientResponse> SimulateEmailQueueAsync()
    {
        return PostAsync($"{BaseUri}/EmailQueue/simulate");
    }

    public Task<ApiClientResponse<ClickHouseStatsResponse>> GetClickHouseStats()
    {
        return GetAsync<ClickHouseStatsResponse>($"{BaseUri}/ClickHouse/Stats");
    }

    public Task<ApiClientResponse<List<ApiMetricsPerMinute>>> GetApiMetricsPerMinute()
    {
        return GetAsync<List<ApiMetricsPerMinute>>($"{BaseUri}/ClickHouse/ApiMetrics");
    }

    public Task<ApiClientResponse<List<SlowEndpointInfo>>> GetSlowEndpoints()
    {
        return GetAsync<List<SlowEndpointInfo>>($"{BaseUri}/ClickHouse/SlowEndpoints");
    }

    public Task<ApiClientResponse<CubeResultSet>> GetCubeExpenses(
        string period = "last 6 months",
        string granularity = "month")
    {
        return GetAsync<CubeResultSet>($"{BaseUri}/Cube/Expenses?period={Uri.EscapeDataString(period)}&granularity={Uri.EscapeDataString(granularity)}");
    }

    public Task<ApiClientResponse<CubeResultSet>> GetCubeDebts(string period = "last 6 months")
    {
        return GetAsync<CubeResultSet>($"{BaseUri}/Cube/Debts?period={Uri.EscapeDataString(period)}");
    }

    public Task<ApiClientResponse<CubeResultSet>> GetCubeTrends(
        string granularity = "week",
        string dateRange = "last 3 months")
    {
        return GetAsync<CubeResultSet>($"{BaseUri}/Cube/Trends?granularity={Uri.EscapeDataString(granularity)}&dateRange={Uri.EscapeDataString(dateRange)}");
    }

    public Task<ApiClientResponse<CubeMeta>> GetCubeMeta()
    {
        return GetAsync<CubeMeta>($"{BaseUri}/Cube/Meta");
    }

    public Task<ApiClientResponse<GraphResponse>> GetDebtGraph(int limit = 200)
    {
        return GetAsync<GraphResponse>($"{BaseUri}/neo4j/debt-graph?limit={limit}");
    }

    public Task<ApiClientResponse<GraphResponse>> GetCategoryTree(int limit = 500)
    {
        return GetAsync<GraphResponse>($"{BaseUri}/neo4j/categories?limit={limit}");
    }

    public Task<ApiClientResponse<LakehouseStatsResponse>> GetLakehouseStats()
    {
        return GetAsync<LakehouseStatsResponse>($"{BaseUri}/Lakehouse/Stats");
    }

    public Task<ApiClientResponse<List<Dictionary<string, object?>>>> QueryLakehouseDuckDb(string sql)
    {
        return PostAsync<List<Dictionary<string, object?>>>($"{BaseUri}/Lakehouse/QueryDuckDb", new LakehouseQueryRequest { Sql = sql });
    }

    public Task<ApiClientResponse<List<Dictionary<string, object?>>>> QueryLakehouseTrino(string sql)
    {
        return PostAsync<List<Dictionary<string, object?>>>($"{BaseUri}/Lakehouse/QueryTrino", new LakehouseQueryRequest { Sql = sql });
    }

    public Task<ApiClientResponse> ForceLakehouseSync()
    {
        return PostAsync($"{BaseUri}/Lakehouse/Sync");
    }

    public Task<ApiClientResponse> ForceLakehouseTransform()
    {
        return PostAsync($"{BaseUri}/Lakehouse/Transform");
    }

    public Task<ApiClientResponse> ForceLakehouseReconciliation()
    {
        return PostAsync($"{BaseUri}/Lakehouse/Reconcile");
    }

    public class CubeResultSet
    {
        public List<Dictionary<string, object?>> Data { get; set; } = [];
        public Dictionary<string, CubeAnnotation>? Annotation { get; set; }
    }

    public record CubeAnnotation(string Title, string ShortTitle, string Type);

    public class CubeMeta
    {
        public List<CubeDef> Cubes { get; set; } = [];
    }

    public record CubeDef(string Name, List<CubeMemberDef> Measures, List<CubeMemberDef> Dimensions);

    public record CubeMemberDef(string Name, string Title, string ShortTitle, string Type);

    public class GraphResponse
    {
        public List<GraphNode> Nodes { get; set; } = [];
        public List<GraphEdge> Edges { get; set; } = [];
    }

    public class GraphNode
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public Dictionary<string, object> Properties { get; set; } = [];
    }

    public class GraphEdge
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Type { get; set; } = "";
        public Dictionary<string, object> Properties { get; set; } = [];
    }

    public class ShardsMetricsResponse
    {
        public Dictionary<string, ShardMetrics> Shards { get; set; } = [];
        public string CurrentUserShard { get; set; } = "";
    }

    public class ShardMetrics
    {
        public List<TableMetrics> Tables { get; set; } = [];
        public long TotalRows { get; set; }
        public long SizeBytes { get; set; }
        public long DbSizeBytes { get; set; }
    }

    public class TableMetrics
    {
        public string Name { get; set; } = "";
        public long LiveRows { get; set; }
        public long DeadRows { get; set; }
        public long SizeBytes { get; set; }
    }

    public class UserShardInfo
    {
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public string ShardName { get; set; } = "";
        public DateTime AssignedAt { get; set; }
    }

    public class PartitionListResponse
    {
        public string Shard { get; set; } = "";
        public DateTimeOffset LastMaintenanceUtc { get; set; }
        public List<PartitionInfo> Partitions { get; set; } = [];
    }

    public class PartitionInfo
    {
        public string Name { get; set; } = "";
        public long ApproxRows { get; set; }
        public long SizeBytes { get; set; }
        public DateOnly RangeStart { get; set; }
        public DateOnly RangeEnd { get; set; }
    }

    public class CacheStatsResponse
    {
        public long TotalKeys { get; set; }
        public long HitsTotal { get; set; }
        public long MissesTotal { get; set; }
        public double HitRatio { get; set; }
        public long UsedMemoryBytes { get; set; }
        public string UsedMemoryHuman { get; set; } = "";
    }

    public class CacheCounterInfo
    {
        public string Key { get; set; } = "";
        public string ShardName { get; set; } = "";
        public string EntityType { get; set; } = "";
        public int UserId { get; set; }
        public long CurrentValue { get; set; }
    }

    public class LockStatsResponse
    {
        public long Acquired { get; set; }
        public long Failed { get; set; }
    }

    public class CacheCategoryInfo
    {
        public string Key { get; set; } = "";
        public string ShardName { get; set; } = "";
        public int UserId { get; set; }
        public double? TtlRemainingSeconds { get; set; }
    }

    public class CacheOperationIndexInfo
    {
        public string ShardName { get; set; } = "";
        public int UserId { get; set; }
        public int CachedFilterCount { get; set; }
        public double? AvgTtlSeconds { get; set; }
    }

    public class PubSubChannelInfo
    {
        public string Channel { get; set; } = "";
        public long Subscribers { get; set; }
    }

    public class PubSubMetricsResponse
    {
        public long PatternSubscribers { get; set; }
    }

    public class EmailQueueStatsResponse
    {
        public long QueueLength { get; set; }
        public long RetryLength { get; set; }
        public long DlqLength { get; set; }
        public List<EmailPreview> RecentMessages { get; set; } = [];
        public List<EmailPreview> RetryMessages { get; set; } = [];
        public List<EmailPreview> DlqMessages { get; set; } = [];
    }

    public class EmailPreview
    {
        public Guid Id { get; set; }
        public string ReceiverEmail { get; set; } = "";
        public string Title { get; set; } = "";
        public int RetryCount { get; set; }
        public DateTimeOffset EnqueuedAt { get; set; }
        public DateTimeOffset? NextRetryAt { get; set; }
    }

    public class ClickHouseStatsResponse
    {
        public List<ClickHouseTableInfo> Tables { get; set; } = [];
        public DateTimeOffset? LastSyncUtc { get; set; }
    }

    public class ClickHouseTableInfo
    {
        public string Name { get; set; } = "";
        public long Rows { get; set; }
        public long Bytes { get; set; }
        public int Partitions { get; set; }
    }

    public class ApiMetricsPerMinute
    {
        public DateTime Minute { get; set; }
        public long Requests { get; set; }
    }

    public class SlowEndpointInfo
    {
        public string Endpoint { get; set; } = "";
        public double AvgDurationMs { get; set; }
        public double P95DurationMs { get; set; }
        public double ErrorRatePercent { get; set; }
    }

    public class LakehouseStatsResponse
    {
        public List<LakehouseLayerInfo> Layers { get; set; } = [];
        public DateTimeOffset? LastSyncUtc { get; set; }
        public long TotalEventsProcessed { get; set; }
    }

    public class LakehouseLayerInfo
    {
        public string Name { get; set; } = "";
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
    }

    public class LakehouseQueryRequest
    {
        public string Sql { get; set; } = "";
    }
}
