using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.ApiClient;
using Money.Business.Enums;
using Neo4j.Driver;

namespace Money.Api.Tests.Neo4j;

public class CategoryGraphTests
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
    public async Task CategoryCreation_SyncsToNeo4j()
    {
        _dbClient.Save();

        var request = new CategoriesClient.SaveRequest
        {
            Name = "НовыйГрафТест",
            OperationTypeId = (int)OperationTypes.Costs,
        };

        var categoryId = await _apiClient.Categories.Create(request).IsSuccessWithContent();

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (c:Category {userId:$userId, id:$categoryId}) RETURN c.name AS name",
                new { userId = $"{_user.ShardName}_{_user.Id}", categoryId });

            return await cursor.ToListAsync();
        });

        Assert.That(result, Has.Some.Matches<IRecord>(r => r["name"].As<string>() == "НовыйГрафТест"));
    }

    [Test]
    public async Task CategoryDeletion_RemovesFromGraph()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Categories.Delete(category.Id).IsSuccess();

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (c:Category {userId:$userId, id:$categoryId}) RETURN count(c) AS cnt",
                new { userId = $"{_user.ShardName}_{_user.Id}", categoryId = category.Id });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.EqualTo(0));
    }

    [Test]
    public async Task CategoryHierarchy_SyncsToNeo4j()
    {
        var parent = _user.WithCategory();
        _dbClient.Save();

        var childRequest = new CategoriesClient.SaveRequest
        {
            Name = "ГрафТакси",
            OperationTypeId = (int)parent.OperationType,
            ParentId = parent.Id,
        };

        var childId = await _apiClient.Categories.Create(childRequest).IsSuccessWithContent();

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (c:Category {userId:$userId, id:$childId})-[:SUBCATEGORY_OF]->(p:Category {id:$parentId}) RETURN count(*) AS cnt",
                new { userId = $"{_user.ShardName}_{_user.Id}", childId, parentId = parent.Id });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.EqualTo(1));
    }
}
