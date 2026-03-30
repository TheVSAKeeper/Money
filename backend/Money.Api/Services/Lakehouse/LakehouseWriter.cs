using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Money.Data.Entities;
using Parquet.Serialization;

namespace Money.Api.Services.Lakehouse;

public sealed class LakehouseWriter(
    IMinioClient minio,
    IOptions<LakehouseSettings> settings,
    ILogger<LakehouseWriter> logger)
{
    public async Task WriteBronzeAsync(
        string shardName, string eventType, List<OutboxEvent> events, CancellationToken ct)
    {
        var records = events.Select(e => new BronzeRecord
        {
            EventId = e.Id,
            ShardName = shardName,
            EventType = e.EventType,
            Payload = e.Payload,
            CreatedAt = e.CreatedAt,
            IngestedAt = DateTime.UtcNow,
        }).ToList();

        using var ms = new MemoryStream();
        await ParquetSerializer.SerializeAsync(records, ms);
        ms.Position = 0;

        var bucket = settings.Value.Warehouse.Replace("s3://", "").TrimEnd('/');
        var objectKey = $"bronze/{eventType}/{shardName}/{DateTime.UtcNow:yyyy'/'MM'/'dd}/{DateTime.UtcNow:HHmmss}_{Guid.NewGuid():N}.parquet";

        await minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(ms)
            .WithObjectSize(ms.Length)
            .WithContentType("application/octet-stream"), ct);

        logger.LogDebug("Written {Key} ({Bytes} bytes, {Count} records)",
            objectKey, ms.Length, records.Count);
    }

    public record BronzeRecord
    {
        public long EventId { get; init; }
        public string ShardName { get; init; } = "";
        public string EventType { get; init; } = "";
        public string Payload { get; init; } = "";
        public DateTime CreatedAt { get; init; }
        public DateTime IngestedAt { get; init; }
    }
}
