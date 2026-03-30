using DuckDB.NET.Data;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Money.Api.Services.Lakehouse;

public sealed class LakehouseQueryService(
    IMinioClient minio,
    IHttpClientFactory httpClientFactory,
    IOptions<LakehouseSettings> settings,
    ILogger<LakehouseQueryService> logger)
{
    private const string Bucket = "lakehouse-warehouse";
    private const int MaxSqlLength = 10_000;

    public async Task<List<Dictionary<string, object?>>> QueryDuckDbAsync(string sql, CancellationToken ct)
    {
        ValidateQuery(sql);

        await using var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var installCmd = connection.CreateCommand();
        installCmd.CommandText = "INSTALL httpfs; LOAD httpfs;";
        await installCmd.ExecuteNonQueryAsync(ct);

        await ConfigureDuckDbS3Async(connection, ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            results.Add(row);
        }

        return results;
    }

    public async Task<List<Dictionary<string, object?>>> QueryTrinoAsync(string sql, CancellationToken ct)
    {
        ValidateQuery(sql);

        var client = httpClientFactory.CreateClient("trino");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/statement");
        request.Content = new StringContent(sql);
        request.Headers.Add("X-Trino-User", "money");
        request.Headers.Add("X-Trino-Catalog", "iceberg");
        request.Headers.Add("X-Trino-Schema", "default");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<TrinoResponse>(ct);

        if (json == null)
        {
            throw new InvalidOperationException("Trino returned an empty response.");
        }

        if (json.Error != null)
        {
            throw new InvalidOperationException($"Trino error {json.Error.ErrorCode}: {json.Error.Message}");
        }

        List<TrinoColumn>? columns = json.Columns;
        var results = new List<Dictionary<string, object?>>();

        CollectRows(json, columns, results);

        var delayMs = 100;

        while (json.NextUri != null)
        {
            await Task.Delay(delayMs, ct);
            delayMs = Math.Min(delayMs * 2, 2000);

            using var nextRequest = new HttpRequestMessage(HttpMethod.Get, json.NextUri);
            using var nextResponse = await client.SendAsync(nextRequest, ct);
            nextResponse.EnsureSuccessStatusCode();

            json = await nextResponse.Content.ReadFromJsonAsync<TrinoResponse>(ct);

            if (json == null)
            {
                throw new InvalidOperationException("Trino returned an empty response during pagination.");
            }

            if (json.Error != null)
            {
                throw new InvalidOperationException($"Trino error {json.Error.ErrorCode}: {json.Error.Message}");
            }

            columns ??= json.Columns;
            CollectRows(json, columns, results);
        }

        return results;
    }

    public async Task ExecuteTrinoAsync(string sql, CancellationToken ct)
    {
        await QueryTrinoAsync(sql, ct);
    }

    internal async Task ConfigureDuckDbS3Async(DuckDBConnection connection, CancellationToken ct)
    {
        var s3 = settings.Value;

        await ExecuteSetAsync(connection, "s3_endpoint", s3.MinioEndpoint, ct);
        await ExecuteSetAsync(connection, "s3_access_key_id", s3.MinioAccessKey, ct);
        await ExecuteSetAsync(connection, "s3_secret_access_key", s3.MinioSecretKey, ct);
        await ExecuteSetAsync(connection, "s3_use_ssl", "false", ct);
        await ExecuteSetAsync(connection, "s3_url_style", "path", ct);
    }

    private static async Task ExecuteSetAsync(
        DuckDBConnection connection, string key, string value, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET {key}='{value.Replace("'", "''")}';";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void ValidateQuery(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (sql.Length > MaxSqlLength)
        {
            throw new ArgumentException($"SQL query exceeds maximum allowed length of {MaxSqlLength} characters.");
        }
    }

    private static void CollectRows(TrinoResponse json, List<TrinoColumn>? columns, List<Dictionary<string, object?>> results)
    {
        if (json.Data == null || columns == null)
        {
            return;
        }

        foreach (var row in json.Data)
        {
            var dict = new Dictionary<string, object?>();

            for (var i = 0; i < columns.Count && i < row.Count; i++)
            {
                dict[columns[i].Name] = row[i];
            }

            results.Add(dict);
        }
    }

    public async Task<LakehouseStorageInfo> GetStorageInfoAsync(CancellationToken ct)
    {
        var layers = new[] { "bronze", "silver", "gold" };
        var info = new LakehouseStorageInfo();

        foreach (var layer in layers)
        {
            long totalBytes = 0;
            var fileCount = 0;

            var args = new ListObjectsArgs().WithBucket(Bucket).WithPrefix($"{layer}/").WithRecursive(true);

            await foreach (var item in minio.ListObjectsEnumAsync(args, ct))
            {
                totalBytes += (long)item.Size;
                fileCount++;
            }

            info.Layers.Add(new LakehouseLayerInfo
            {
                Name = layer,
                FileCount = fileCount,
                TotalBytes = totalBytes,
            });
        }

        return info;
    }

    private sealed class TrinoResponse
    {
        public List<TrinoColumn>? Columns { get; set; }
        public List<List<object?>>? Data { get; set; }
        public string? NextUri { get; set; }
        public TrinoError? Error { get; set; }
    }

    private sealed class TrinoError
    {
        public string? Message { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorName { get; set; }
        public string? ErrorType { get; set; }
    }

    private sealed class TrinoColumn
    {
        public string Name { get; set; } = "";
    }
}

public class LakehouseStorageInfo
{
    public List<LakehouseLayerInfo> Layers { get; set; } = [];
}

public class LakehouseLayerInfo
{
    public string Name { get; set; } = "";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
}
