using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.ApiClient;
using Neo4j.Driver;

namespace Money.Api.Tests.Neo4j;

public class DebtGraphTests
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
    public async Task DebtCreation_SyncsToNeo4j()
    {
        _dbClient.Save();

        var request = new DebtClient.SaveRequest
        {
            OwnerName = "Иван",
            Sum = 5000,
            Date = DateTime.Now.Date,
            TypeId = 1,
        };

        await _apiClient.Debts.Create(request).IsSuccessWithContent();

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (u:User {userId:$userId})-[r:OWES]->(o:DebtOwner) RETURN r.amountCents AS amountCents",
                new { userId = $"{_user.ShardName}_{_user.Id}" });

            return await cursor.ToListAsync();
        });

        Assert.That(result, Has.Some.Matches<IRecord>(r => r["amountCents"].As<long>() == 500000));
    }

    [Test]
    public async Task DebtDeletion_RemovesFromGraph()
    {
        var debt = _user.WithDebt();
        _dbClient.Save();

        // Sync initial debt to Neo4j
        await _syncService.RunSyncAsync(CancellationToken.None);

        await _apiClient.Debts.Delete(debt.Id).IsSuccess();

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (:User {userId:$userId})-[r:OWES {debtId:$debtId}]->() RETURN count(r) AS cnt",
                new { userId = $"{_user.ShardName}_{_user.Id}", debtId = debt.Id });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.EqualTo(0));
    }

    [Test]
    public async Task DebtPayment_UpdatesGraphEdge()
    {
        _dbClient.Save();

        var createRequest = new DebtClient.SaveRequest
        {
            OwnerName = "ПлатёжТест",
            Sum = 1000,
            Date = DateTime.Now.Date,
            TypeId = 1,
        };

        var debtId = await _apiClient.Debts.Create(createRequest).IsSuccessWithContent();

        await _syncService.RunSyncAsync(CancellationToken.None);

        var payRequest = new DebtClient.PayRequest
        {
            Sum = 500,
            Comment = "Половину вернул",
            Date = DateTime.Now.Date,
        };

        await _apiClient.Debts.Pay(debtId, payRequest).IsSuccess();

        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (:User {userId:$userId})-[r:OWES {debtId:$debtId}]->() RETURN r.paySumCents AS paySumCents",
                new
                {
                    userId = $"{_user.ShardName}_{_user.Id}",
                    debtId,
                });

            return await cursor.ToListAsync();
        });

        Assert.That(result, Has.Some.Matches<IRecord>(r => r["paySumCents"].As<long>() == 50000));
    }

    [Test]
    public async Task MergeOwners_MergesNodesInGraph()
    {
        var debt1 = _user.WithDebt().SetOwnerName("ГрафUser1");
        var debt2 = _user.WithDebt().SetOwnerName("ГрафUser2");
        _dbClient.Save();

        await _syncService.RunSyncAsync(CancellationToken.None);
        await _apiClient.Debts.MergeOwners(debt1.OwnerId, debt2.OwnerId).IsSuccess();
        await _syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();

        var fromResult = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (o:DebtOwner {userId:$userId, name:$name}) RETURN count(o) AS cnt",
                new { userId = $"{_user.ShardName}_{_user.Id}", name = "ГрафUser1" });

            return await cursor.SingleAsync();
        });

        Assert.That(fromResult["cnt"].As<int>(), Is.EqualTo(0));
    }
}
