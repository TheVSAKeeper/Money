using System.Collections.Concurrent;

namespace Money.Api.Services.Analytics;

public class ApiMetricsQueue
{
    public ConcurrentQueue<ApiMetric> Metrics { get; } = new();
}

public class ApiMetric
{
    public DateTime Timestamp { get; set; }
    public int? UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string Method { get; set; } = "";
    public int StatusCode { get; set; }
    public double DurationMs { get; set; }
}
