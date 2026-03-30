using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using Money.Api.BackgroundServices;
using Money.Api.Services.Lakehouse;
using Money.ApiClient;

namespace Money.Api.Tests.Tests.Lakehouse;

public class LakehouseTransformTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
#pragma warning disable NUnit1032
    private LakehouseSyncService _syncService = null!;
    private LakehouseTransformService _transformService = null!;
    private IMinioClient _minio = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);

        _syncService = Integration.ServiceProvider.GetRequiredService<LakehouseSyncService>();
        _transformService = Integration.ServiceProvider.GetRequiredService<LakehouseTransformService>();
        _minio = Integration.ServiceProvider.GetRequiredService<IMinioClient>();
    }

    [Test]
    public async Task Transform_BronzeToSilver_CreatesSilverParquetFiles()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Operations.Create(new()
            {
                CategoryId = category.Id,
                Sum = 150,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();
        await _transformService.TransformBronzeToSilverAsync(CancellationToken.None);

        var objects = await ListObjectsAsync("silver/operations/");

        Assert.That(objects, Is.Not.Empty, "Silver operations parquet files should exist after bronze→silver transform");
    }

    [Test]
    public async Task Transform_BronzeToSilver_IsIdempotent()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Operations.Create(new()
            {
                CategoryId = category.Id,
                Sum = 200,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();
        await _transformService.TransformBronzeToSilverAsync(CancellationToken.None);

        var countAfterFirst = (await ListObjectsAsync("silver/")).Count;

        await _transformService.TransformBronzeToSilverAsync(CancellationToken.None);

        var countAfterSecond = (await ListObjectsAsync("silver/")).Count;

        Assert.That(countAfterSecond, Is.EqualTo(countAfterFirst),
            "Second transform should produce same number of silver files when no new bronze data was added");
    }

    [Test]
    public async Task Transform_SilverToGold_SkipsWhenNoSilverData()
    {
        await ClearObjectsAsync("silver/");

        Assert.DoesNotThrowAsync(
            async () => await _transformService.TransformSilverToGoldAsync(CancellationToken.None),
            "TransformSilverToGoldAsync should not throw when no silver data is available");
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
                objects.Add(item.Key);
        }

        return objects;
    }

    private async Task ClearObjectsAsync(string prefix)
    {
        var objects = await ListObjectsAsync(prefix);

        foreach (var key in objects)
        {
            await _minio.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket("lakehouse-warehouse")
                .WithObject(key));
        }
    }
}
