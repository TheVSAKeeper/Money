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
}
