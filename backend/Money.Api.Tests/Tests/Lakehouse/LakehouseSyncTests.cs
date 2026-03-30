using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using Money.Api.BackgroundServices;
using Money.Api.Services.Analytics;
using Money.ApiClient;

namespace Money.Api.Tests.Tests.Lakehouse;

public class LakehouseSyncTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
#pragma warning disable NUnit1032
    private LakehouseSyncService _syncService = null!;
    private IMinioClient _minio = null!;
#pragma warning restore NUnit1032
    private OutboxCursorService _cursorService = null!;

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);

        _syncService = Integration.ServiceProvider.GetRequiredService<LakehouseSyncService>();
        _minio = Integration.ServiceProvider.GetRequiredService<IMinioClient>();
        _cursorService = Integration.ServiceProvider.GetRequiredService<OutboxCursorService>();
    }

    [Test]
    public async Task LakehouseSync_WritesParquetToMinIO_AfterOperationCreate()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Operations.Create(new()
            {
                CategoryId = category.Id,
                Sum = 100,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();

        var objects = await ListObjectsAsync("bronze/operations/");

        Assert.That(objects, Is.Not.Empty, "Bronze operations parquet files should exist after sync");
    }

    [Test]
    public async Task LakehouseSync_WritesDebtEvents_AfterDebtCreate()
    {
        _dbClient.Save();

        await _apiClient.Debts.Create(new()
            {
                OwnerName = "Test Debt Owner",
                Sum = 500,
                Date = DateTime.Today,
                TypeId = 1,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();

        var objects = await ListObjectsAsync("bronze/debts/");

        Assert.That(objects, Is.Not.Empty, "Bronze debts parquet files should exist after sync");
    }

    [Test]
    public async Task LakehouseSync_WritesCategoryEvents_AfterCategoryCreate()
    {
        _dbClient.Save();

        await _apiClient.Categories.Create(new()
            {
                Name = "LakehouseTestCategory",
                OperationTypeId = 2,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();

        var objects = await ListObjectsAsync("bronze/categories/");

        Assert.That(objects, Is.Not.Empty, "Bronze categories parquet files should exist after sync");
    }

    [Test]
    public async Task LakehouseSync_AdvancesCursor_AfterProcessing()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Operations.Create(new()
            {
                CategoryId = category.Id,
                Sum = 250,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        var cursorBefore = await _cursorService.GetCursorAsync(LakehouseSyncService.ConsumerName, _user.ShardName);

        await _syncService.RunSyncAsync();

        var cursorAfter = await _cursorService.GetCursorAsync(LakehouseSyncService.ConsumerName, _user.ShardName);

        Assert.That(cursorAfter, Is.GreaterThan(cursorBefore), "Cursor should advance after sync");
    }

    [Test]
    public async Task LakehouseSync_UpdatesLastSyncUtc()
    {
        _dbClient.Save();

        var before = _syncService.LastSyncUtc;

        await _apiClient.Operations.Create(new()
            {
                CategoryId = _user.WithCategory().Also(_ => _dbClient.Save()).Id,
                Sum = 50,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();

        Assert.That(_syncService.LastSyncUtc, Is.Not.Null);
        Assert.That(_syncService.LastSyncUtc, Is.GreaterThan(before ?? DateTimeOffset.MinValue));
    }

    [Test]
    public async Task LakehouseSync_IncrementsTotalEventsProcessed()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var before = _syncService.TotalEventsProcessed;

        await _apiClient.Operations.Create(new()
            {
                CategoryId = category.Id,
                Sum = 75,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();

        Assert.That(_syncService.TotalEventsProcessed, Is.GreaterThan(before));
    }

    [Test]
    public async Task LakehouseSync_IdempotentOnSecondRun()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Operations.Create(new()
            {
                CategoryId = category.Id,
                Sum = 300,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();
        var countAfterFirst = (await ListObjectsAsync("bronze/")).Count;

        await _syncService.RunSyncAsync();
        var countAfterSecond = (await ListObjectsAsync("bronze/")).Count;

        Assert.That(countAfterSecond, Is.EqualTo(countAfterFirst),
            "Second sync should not produce new files when no new events exist");
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

internal static class TestExtensions
{
    public static T Also<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }
}
