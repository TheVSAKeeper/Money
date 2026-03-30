using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using Money.Api.Services.Lakehouse;
using Money.Data.Entities;

namespace Money.Api.Tests.Tests.Lakehouse;

public class LakehouseWriterTests
{
#pragma warning disable NUnit1032
    private IMinioClient _minio = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _minio = Integration.ServiceProvider.GetRequiredService<IMinioClient>();
    }

    [Test]
    public async Task WriteBronze_CreatesParquetFile_WithCorrectPath()
    {
        using var scope = Integration.ServiceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<LakehouseWriter>();

        var events = new List<OutboxEvent>
        {
            new()
            {
                Id = 99900 + Random.Shared.Next(1000),
                EventType = OutboxEvent.OperationType,
                Payload = """{"UserId":1,"OperationId":1,"CategoryId":1,"Sum":100,"Action":"added"}""",
                CreatedAt = DateTime.UtcNow,
            },
        };

        await writer.WriteBronzeAsync("test-shard", "operations", events, CancellationToken.None);

        var objects = await ListObjectsAsync("bronze/operations/test-shard/");

        Assert.That(objects, Is.Not.Empty, "Parquet file should be created in bronze/operations/test-shard/");
    }

    [Test]
    public async Task WriteBronze_MultipleEventTypes_WritesToSeparatePaths()
    {
        using var scope = Integration.ServiceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<LakehouseWriter>();

        var opEvent = new OutboxEvent
        {
            Id = 99800 + Random.Shared.Next(100),
            EventType = OutboxEvent.OperationType,
            Payload = """{"UserId":1,"Sum":50,"Action":"added"}""",
            CreatedAt = DateTime.UtcNow,
        };

        var debtEvent = new OutboxEvent
        {
            Id = 99900 + Random.Shared.Next(100),
            EventType = OutboxEvent.DebtType,
            Payload = """{"UserId":1,"DebtId":1,"Sum":200,"Action":"added"}""",
            CreatedAt = DateTime.UtcNow,
        };

        await writer.WriteBronzeAsync("multi-shard", "operations", [opEvent], CancellationToken.None);
        await writer.WriteBronzeAsync("multi-shard", "debts", [debtEvent], CancellationToken.None);

        var opObjects = await ListObjectsAsync("bronze/operations/multi-shard/");
        var debtObjects = await ListObjectsAsync("bronze/debts/multi-shard/");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(opObjects, Is.Not.Empty, "Operation parquet should exist");
            Assert.That(debtObjects, Is.Not.Empty, "Debt parquet should exist");
        }
    }

    [Test]
    public async Task WriteBronze_ParquetFileHasNonZeroSize()
    {
        using var scope = Integration.ServiceProvider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<LakehouseWriter>();

        var events = Enumerable.Range(1, 10).Select(i => new OutboxEvent
        {
            Id = 88000 + i,
            EventType = OutboxEvent.OperationType,
            Payload = $$$"""{"UserId":1,"OperationId":{{{i}}},"Sum":{{{i * 10}}},"Action":"added"}""",
            CreatedAt = DateTime.UtcNow,
        }).ToList();

        await writer.WriteBronzeAsync("size-shard", "operations", events, CancellationToken.None);

        long totalSize = 0;
        var args = new ListObjectsArgs()
            .WithBucket("lakehouse-warehouse")
            .WithPrefix("bronze/operations/size-shard/")
            .WithRecursive(true);

        await foreach (var item in _minio.ListObjectsEnumAsync(args))
        {
            totalSize += (long)item.Size;
        }

        Assert.That(totalSize, Is.GreaterThan(0), "Parquet files should have non-zero size");
    }

    private async Task<List<string>> ListObjectsAsync(string prefix)
    {
        var objects = new List<string>();
        var args = new ListObjectsArgs()
            .WithBucket("lakehouse-warehouse")
            .WithPrefix(prefix)
            .WithRecursive(true);

        await foreach (var item in _minio.ListObjectsEnumAsync(args))
        {
            if (item.Key.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            {
                objects.Add(item.Key);
            }
        }

        return objects;
    }
}
