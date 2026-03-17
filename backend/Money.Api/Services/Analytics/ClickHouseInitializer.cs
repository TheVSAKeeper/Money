namespace Money.Api.Services.Analytics;

public sealed class ClickHouseInitializer(ClickHouseService clickHouseService, ILogger<ClickHouseInitializer> logger)
{
    private const string CreateOperationsAnalyticsSql =
        """
        CREATE TABLE IF NOT EXISTS operations_analytics (
            user_id      Int32,
            operation_id Int32,
            category_id  Int32,
            category_name String,
            operation_type Int32,
            sum          Decimal(18,2),
            date         Date,
            place_name   Nullable(String),
            comment      Nullable(String),
            created_at   DateTime DEFAULT now()
        ) ENGINE = MergeTree()
        ORDER BY (user_id, date, category_id)
        PARTITION BY toYYYYMM(date)
        """;

    private const string CreateDebtsAnalyticsSql =
        """
        CREATE TABLE IF NOT EXISTS debts_analytics (
            user_id   Int32,
            debt_id   Int32,
            owner_name String,
            type_id   Int32,
            sum       Decimal(18,2),
            pay_sum   Decimal(18,2),
            status_id Int32,
            date      Date,
            created_at DateTime DEFAULT now()
        ) ENGINE = ReplacingMergeTree(created_at)
        ORDER BY (user_id, debt_id)
        PARTITION BY toYYYYMM(date)
        """;

    private const string CreateApiMetricsSql =
        """
        CREATE TABLE IF NOT EXISTS api_metrics (
            timestamp   DateTime,
            user_id     Nullable(Int32),
            endpoint    String,
            method      String,
            status_code Int32,
            duration_ms Float64
        ) ENGINE = MergeTree()
        ORDER BY (endpoint, timestamp)
        PARTITION BY toYYYYMMDD(timestamp)
        TTL timestamp + INTERVAL 90 DAY
        """;

    public async Task InitializeWithRetryAsync(int retries, TimeSpan baseDelay)
    {
        for (var attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                await clickHouseService.ExecuteAsync(CreateOperationsAnalyticsSql);
                await clickHouseService.ExecuteAsync(CreateDebtsAnalyticsSql);
                await clickHouseService.ExecuteAsync(CreateApiMetricsSql);
                logger.LogInformation("ClickHouse tables initialized successfully");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "ClickHouse table initialization attempt {Attempt}/{Retries} failed",
                    attempt + 1,
                    retries);

                if (attempt < retries - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(baseDelay.TotalSeconds * Math.Pow(2, attempt)));
                }
            }
        }

        logger.LogError("Failed to initialize ClickHouse tables after {Retries} attempts", retries);
    }
}
