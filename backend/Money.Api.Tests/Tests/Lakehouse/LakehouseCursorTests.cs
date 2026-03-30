using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.Api.Services.Analytics;
using Money.ApiClient;

namespace Money.Api.Tests.Tests.Lakehouse;

public class LakehouseCursorTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
    private OutboxCursorService _cursorService = null!;
#pragma warning disable NUnit1032
    private LakehouseSyncService _lakehouseSync = null!;
    private ClickHouseSyncService _clickHouseSync = null!;
    private Neo4jSyncService _neo4jSync = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);

        _cursorService = Integration.ServiceProvider.GetRequiredService<OutboxCursorService>();
        _lakehouseSync = Integration.ServiceProvider.GetRequiredService<LakehouseSyncService>();
        _clickHouseSync = Integration.ServiceProvider.GetRequiredService<ClickHouseSyncService>();
        _neo4jSync = Integration.ServiceProvider.GetRequiredService<Neo4jSyncService>();
    }

    [Test]
    public async Task LakehouseCursor_IndependentFromClickHouse()
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

        // Sync both consumers
        await _clickHouseSync.RunSyncAsync();
        await _lakehouseSync.RunSyncAsync();

        var chCursor = await _cursorService.GetCursorAsync(ClickHouseSyncService.ConsumerName, _user.ShardName);
        var lhCursor = await _cursorService.GetCursorAsync(LakehouseSyncService.ConsumerName, _user.ShardName);

        // Both should have advanced independently (separate Redis keys)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(chCursor, Is.GreaterThan(0), "ClickHouse cursor should have advanced");
            Assert.That(lhCursor, Is.GreaterThan(0), "Lakehouse cursor should have advanced");
        }
    }

    [Test]
    public async Task LakehouseCursor_IndependentFromNeo4j()
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

        // Sync both consumers
        await _neo4jSync.RunSyncAsync(CancellationToken.None);
        await _lakehouseSync.RunSyncAsync();

        var neo4jCursor = await _cursorService.GetCursorAsync(Neo4jSyncService.ConsumerName, _user.ShardName);
        var lhCursor = await _cursorService.GetCursorAsync(LakehouseSyncService.ConsumerName, _user.ShardName);

        // Both should have advanced independently (separate Redis keys)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(neo4jCursor, Is.GreaterThan(0), "Neo4j cursor should have advanced");
            Assert.That(lhCursor, Is.GreaterThan(0), "Lakehouse cursor should have advanced");
        }
    }

    [Test]
    public async Task AllThreeConsumers_ProcessSameEvents_Independently()
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

        await Task.WhenAll(
            _clickHouseSync.RunSyncAsync(),
            _neo4jSync.RunSyncAsync(CancellationToken.None),
            _lakehouseSync.RunSyncAsync());

        var chCursor = await _cursorService.GetCursorAsync(ClickHouseSyncService.ConsumerName, _user.ShardName);
        var neo4jCursor = await _cursorService.GetCursorAsync(Neo4jSyncService.ConsumerName, _user.ShardName);
        var lhCursor = await _cursorService.GetCursorAsync(LakehouseSyncService.ConsumerName, _user.ShardName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chCursor, Is.GreaterThan(0), "ClickHouse cursor should be non-zero");
            Assert.That(neo4jCursor, Is.GreaterThan(0), "Neo4j cursor should be non-zero");
            Assert.That(lhCursor, Is.GreaterThan(0), "Lakehouse cursor should be non-zero");
        }
    }
}
