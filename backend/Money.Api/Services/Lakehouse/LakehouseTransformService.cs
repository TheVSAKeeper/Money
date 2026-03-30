using DuckDB.NET.Data;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Money.Api.Services.Lakehouse;

public sealed class LakehouseTransformService(
    IMinioClient minio,
    IOptions<LakehouseSettings> settings,
    LakehouseQueryService queryService,
    ILogger<LakehouseTransformService> logger)
{
    private const string Bucket = "lakehouse-warehouse";

    public async Task TransformBronzeToSilverAsync(CancellationToken ct)
    {
        var bronzeFiles = await ListObjectsAsync("bronze/", ct);

        if (bronzeFiles.Count == 0)
        {
            return;
        }

        logger.LogInformation("Transforming {Count} bronze files to silver", bronzeFiles.Count);

        foreach (var group in bronzeFiles.GroupBy(f => GetEventType(f)))
        {
            await TransformGroupAsync(group.Key, group.ToList(), ct);
        }
    }

    public async Task TransformSilverToGoldAsync(CancellationToken ct)
    {
        var silverFiles = await ListObjectsAsync("silver/", ct);

        if (silverFiles.Count == 0)
        {
            return;
        }

        logger.LogInformation("Aggregating {Count} silver files to gold", silverFiles.Count);

        await using var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var installCmd = connection.CreateCommand();
        installCmd.CommandText = "INSTALL httpfs; LOAD httpfs;";
        await installCmd.ExecuteNonQueryAsync(ct);

        await queryService.ConfigureDuckDbS3Async(connection, ct);

        await AggregateMonthlySpendingAsync(connection, ct);
    }

    private async Task TransformGroupAsync(string eventType, List<string> files, CancellationToken ct)
    {
        await using var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var installCmd = connection.CreateCommand();
        installCmd.CommandText = "INSTALL httpfs; LOAD httpfs;";
        await installCmd.ExecuteNonQueryAsync(ct);

        await queryService.ConfigureDuckDbS3Async(connection, ct);

        var silverPath = $"s3://{Bucket}/silver/{eventType}/{DateTime.UtcNow:yyyy'/'MM'/'dd}";

        await using var transformCmd = connection.CreateCommand();
        transformCmd.CommandText = $"""
            COPY (
                SELECT DISTINCT ON (EventId, ShardName)
                    EventId,
                    ShardName,
                    EventType,
                    json(Payload) AS Payload,
                    CreatedAt,
                    IngestedAt
                FROM read_parquet([{string.Join(",", files.Select(f => $"'s3://{Bucket}/{f}'"))}])
                ORDER BY EventId, ShardName, IngestedAt DESC
            ) TO '{silverPath}' (FORMAT PARQUET, PARTITION_BY (ShardName), OVERWRITE true);
            """;
        await transformCmd.ExecuteNonQueryAsync(ct);

        logger.LogDebug("Transformed {Count} bronze files to silver for {EventType}", files.Count, eventType);
    }

    private async Task AggregateMonthlySpendingAsync(DuckDBConnection connection, CancellationToken ct)
    {
        var silverOperations = await ListObjectsAsync("silver/operations/", ct);

        if (silverOperations.Count == 0)
        {
            return;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                json_extract_string(Payload, '$.UserId')::INT AS user_id,
                json_extract_string(Payload, '$.AuthUserId') AS auth_user_id,
                json_extract_string(Payload, '$.CategoryId')::INT AS category_id,
                COALESCE(NULLIF(json_extract_string(Payload, '$.CategoryName'), ''), 'Без категории') AS category_name,
                date_trunc('month', CreatedAt::TIMESTAMP)::DATE AS month,
                SUM(json_extract_string(Payload, '$.Sum')::DECIMAL) AS total_sum,
                COUNT(*) AS operation_count
            FROM read_parquet([{string.Join(",", silverOperations.Select(f => $"'s3://{Bucket}/{f}'"))}])
            WHERE EventType = 'operation'
            GROUP BY user_id, auth_user_id, category_id, category_name, month
            """;

        await using var reader = (DuckDBDataReader)await cmd.ExecuteReaderAsync(ct);

        var rows = new List<GoldRow>();

        while (await reader.ReadAsync(ct))
        {
            rows.Add(new GoldRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetDateTime(4),
                reader.GetDecimal(5),
                reader.GetInt64(6)));
        }

        if (rows.Count == 0)
        {
            return;
        }

        await queryService.ExecuteTrinoAsync("CREATE SCHEMA IF NOT EXISTS iceberg.gold", ct);

        await queryService.ExecuteTrinoAsync("""
            CREATE TABLE IF NOT EXISTS iceberg.gold.monthly_spending (
                user_id         INT,
                auth_user_id    VARCHAR,
                category_id     INT,
                category_name   VARCHAR,
                month           DATE,
                total_sum       DECIMAL(18,2),
                operation_count BIGINT
            ) WITH (
                location = 's3://lakehouse-warehouse/gold/monthly_spending/',
                format   = 'PARQUET'
            )
            """, ct);

        await queryService.ExecuteTrinoAsync("DELETE FROM iceberg.gold.monthly_spending", ct);

        const int batchSize = 100;
        var totalBatches = (rows.Count + batchSize - 1) / batchSize;

        for (var i = 0; i < rows.Count; i += batchSize)
        {
            var batchIndex = i / batchSize + 1;
            var batch = rows.GetRange(i, Math.Min(batchSize, rows.Count - i));
            var values = string.Join(",\n", batch.Select(r =>
                $"({r.UserId}, '{EscapeString(r.AuthUserId)}', {r.CategoryId}, '{EscapeString(r.CategoryName)}', " +
                $"DATE '{r.Month:yyyy-MM-dd}', {r.TotalSum.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {r.OperationCount})"));
            await queryService.ExecuteTrinoAsync(
                $"INSERT INTO iceberg.gold.monthly_spending VALUES\n{values}", ct);

            logger.LogDebug("Gold INSERT batch {Batch}/{Total}: {Count} rows",
                batchIndex, totalBatches, batch.Count);
        }

        logger.LogInformation("Gold monthly_spending written to Iceberg via Trino: {Count} rows", rows.Count);
    }

    private static string EscapeString(string value) => value.Replace("'", "''");

    private async Task<List<string>> ListObjectsAsync(string prefix, CancellationToken ct)
    {
        var objects = new List<string>();
        var args = new ListObjectsArgs().WithBucket(Bucket).WithPrefix(prefix).WithRecursive(true);

        await foreach (var item in minio.ListObjectsEnumAsync(args, ct))
        {
            if (item.Key.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            {
                objects.Add(item.Key);
            }
        }

        return objects;
    }

    private static string GetEventType(string path)
    {
        var parts = path.Split('/');
        return parts.Length > 1 ? parts[1] : "unknown";
    }

    private record GoldRow(int UserId, string AuthUserId, int CategoryId, string CategoryName, DateTime Month, decimal TotalSum, long OperationCount);
}
