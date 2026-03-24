using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.ApiClient;
using Money.Business.Enums;
using Neo4j.Driver;

namespace Money.Api.Tests.Neo4j;

public class TenantIsolationTests
{
    [Test]
    public async Task CategoryGraph_DoesNotLeakBetweenUsers()
    {
        var dbClient1 = Integration.GetDatabaseClient();
        var user1 = dbClient1.WithUser();
        var apiClient1 = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
        apiClient1.SetUser(user1);

        var dbClient2 = Integration.GetDatabaseClient();
        var user2 = dbClient2.WithUser();

        dbClient1.Save();
        dbClient2.Save();

        await apiClient1.Categories.Create(new()
            {
                Name = "КатегорияТенант",
                OperationTypeId = (int)OperationTypes.Costs,
            })
            .IsSuccessWithContent();

        var syncService = Integration.ServiceProvider.GetRequiredService<Neo4jSyncService>();
        await syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (c:Category {userId:$userId}) RETURN count(c) AS cnt",
                new { userId = $"{user2.ShardName}_{user2.Id}" });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.EqualTo(0));
    }

    [Test]
    public async Task DebtGraph_DoesNotLeakBetweenUsers()
    {
        var dbClient1 = Integration.GetDatabaseClient();
        var user1 = dbClient1.WithUser();
        var apiClient1 = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
        apiClient1.SetUser(user1);

        var dbClient2 = Integration.GetDatabaseClient();
        var user2 = dbClient2.WithUser();
        var apiClient2 = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);
        apiClient2.SetUser(user2);

        dbClient1.Save();
        dbClient2.Save();

        var request = new DebtClient.SaveRequest
        {
            OwnerName = "ТенантТест",
            Sum = 1000,
            Date = DateTime.Now.Date,
            TypeId = 1,
        };

        await apiClient1.Debts.Create(request).IsSuccessWithContent();

        var syncService = Integration.ServiceProvider.GetRequiredService<Neo4jSyncService>();
        await syncService.RunSyncAsync(CancellationToken.None);

        await using var session = Integration.GetNeo4jDriver().AsyncSession();
        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (u:User {userId:$userId})-[r:OWES]->(o:DebtOwner) RETURN count(r) AS cnt",
                new { userId = $"{user2.ShardName}_{user2.Id}" });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.EqualTo(0));
    }
}
