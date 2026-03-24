using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.ApiClient;
using Neo4j.Driver;

namespace Money.Api.Tests.Neo4j;

public class GraphFlowSyncTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
#pragma warning disable NUnit1032
    private Neo4jSyncService _syncService = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);
        _syncService = Integration.ServiceProvider.GetRequiredService<Neo4jSyncService>();
    }

    [Test]
    public async Task GraphFlowSync_DoesNotLeakBetweenUsers()
    {
        var dbClient2 = Integration.GetDatabaseClient();
        var user2 = dbClient2.WithUser();
        var category2 = user2.WithCategory();
        dbClient2.Save();

        var category1 = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Operations.Create(new()
            {
                CategoryId = category1.Id,
                Sum = 100,
                Date = DateTime.Today,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (c:Category {userId:$userId, id:$categoryId})-[r:HAS_OPERATIONS]->() RETURN count(r) AS cnt",
                new { userId = $"{user2.ShardName}_{user2.Id}", categoryId = category2.Id });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.EqualTo(0));
    }

    [Test]
    public async Task GraphFlowSync_FlushesToNeo4j()
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

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (c:Category {userId:$userId, id:$categoryId})-[r:HAS_OPERATIONS]->() RETURN count(r) AS cnt",
                new { userId = $"{_user.ShardName}_{_user.Id}", categoryId = category.Id });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task GraphFlowSync_SumAccumulatesCorrectly()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var amounts = new[] { 100m, 200m, 300m };

        foreach (var amount in amounts)
        {
            await _apiClient.Operations.Create(new()
                {
                    CategoryId = category.Id,
                    Sum = amount,
                    Date = DateTime.Today,
                })
                .IsSuccessWithContent();
        }

        await _syncService.RunSyncAsync(CancellationToken.None);

        var expectedCents = (long)(amounts.Sum() * 100);
        var yearMonth = DateTime.Today.ToString("yyyy-MM");

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (c:Category {userId:$userId, id:$categoryId})-[r:HAS_OPERATIONS]->(m:Month {value:$yearMonth}) RETURN r.totalSumCents AS totalSumCents",
                new { userId = $"{_user.ShardName}_{_user.Id}", categoryId = category.Id, yearMonth });

            return await cursor.ToListAsync();
        });

        Assert.That(result, Has.Some.Matches<IRecord>(r => r["totalSumCents"].As<long>() == expectedCents));
    }
}
