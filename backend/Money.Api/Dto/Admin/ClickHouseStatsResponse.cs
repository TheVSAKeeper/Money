namespace Money.Api.Dto.Admin;

public class ClickHouseStatsResponse
{
    public List<ClickHouseTableInfo> Tables { get; init; } = [];
    public DateTimeOffset? LastSyncUtc { get; init; }
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
