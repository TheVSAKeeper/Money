namespace Money.Api.Dto.Admin;

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
