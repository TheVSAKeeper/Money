using Microsoft.Extensions.Options;
using Money.Api.Services.Analytics;

namespace Money.Api.BackgroundServices;

public sealed class ClickHouseMetricsService(
    ClickHouseService clickHouse,
    ApiMetricsQueue queue,
    IOptions<ClickHouseSettings> settings,
    ILogger<ClickHouseMetricsService> logger) : BackgroundService
{
    public async Task RunFlushAsync(CancellationToken ct = default)
    {
        var batch = new List<ApiMetric>();

        while (queue.Metrics.TryDequeue(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
        {
            return;
        }

        logger.LogDebug("Flushing {Count} API metrics to ClickHouse", batch.Count);

        var rows = batch.Select(m => new[]
        {
            m.Timestamp,
            m.UserId.HasValue ? (object)m.UserId.Value : DBNull.Value,
            m.Endpoint,
            m.Method,
            m.StatusCode,
            m.DurationMs,
        });

        try
        {
            await clickHouse.InsertBatchAsync("api_metrics (timestamp, user_id, endpoint, method, status_code, duration_ms)",
                rows,
                ct);
        }
        catch (Exception)
        {
            foreach (var metric in batch)
            {
                queue.Metrics.Enqueue(metric);
            }

            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.Value.SyncIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeFlushAsync(stoppingToken);
        }

        await SafeFlushAsync(CancellationToken.None);
    }

    private async Task SafeFlushAsync(CancellationToken ct)
    {
        try
        {
            await RunFlushAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ClickHouse metrics flush failed, metrics remain in queue for next iteration");
        }
    }
}
