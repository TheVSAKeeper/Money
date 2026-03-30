using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.ApiClient;

namespace Money.Api.Tests.Tests.Lakehouse;

public class LakehouseApiTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
#pragma warning disable NUnit1032
    private LakehouseSyncService _syncService = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);

        _syncService = Integration.ServiceProvider.GetRequiredService<LakehouseSyncService>();
    }

    [Test]
    public async Task LakehouseApi_Stats_ReturnsLayerInfo()
    {
        _dbClient.Save();

        var result = await _apiClient.Admin.GetLakehouseStats();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessStatusCode, Is.True);
            Assert.That(result.Content, Is.Not.Null);
            Assert.That(result.Content!.Layers, Is.Not.Empty);
            Assert.That(result.Content.Layers.Select(l => l.Name), Does.Contain("bronze"));
            Assert.That(result.Content.Layers.Select(l => l.Name), Does.Contain("silver"));
            Assert.That(result.Content.Layers.Select(l => l.Name), Does.Contain("gold"));
        }
    }

    [Test]
    public async Task LakehouseApi_Stats_RequiresAuthentication()
    {
        var unauthClient = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);

        var result = await unauthClient.Admin.GetLakehouseStats();

        Assert.That(result.IsSuccessStatusCode, Is.False);
    }

    [Test]
    public async Task LakehouseApi_ForceSync_ReturnsNoContent()
    {
        _dbClient.Save();

        var result = await _apiClient.Admin.ForceLakehouseSync();

        Assert.That(result.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task LakehouseApi_Stats_ReflectsSyncState()
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

        var result = await _apiClient.Admin.GetLakehouseStats();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessStatusCode, Is.True);
            Assert.That(result.Content!.LastSyncUtc, Is.Not.Null);
            Assert.That(result.Content.TotalEventsProcessed, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task LakehouseApi_Stats_ShowsBronzeFileCount_AfterSync()
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

        var result = await _apiClient.Admin.GetLakehouseStats();

        var bronzeLayer = result.Content!.Layers.FirstOrDefault(l => l.Name == "bronze");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bronzeLayer, Is.Not.Null);
            Assert.That(bronzeLayer!.FileCount, Is.GreaterThan(0));
            Assert.That(bronzeLayer.TotalBytes, Is.GreaterThan(0));
        }
    }
}
