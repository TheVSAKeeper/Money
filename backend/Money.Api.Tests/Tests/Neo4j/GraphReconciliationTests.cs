using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Neo4j.Driver;

namespace Money.Api.Tests.Neo4j;

public class GraphReconciliationTests
{
    [Test]
    public async Task Reconciliation_SyncsExistingData()
    {
        var dbClient = Integration.GetDatabaseClient();
        var user = dbClient.WithUser();
        var debt = user.WithDebt().SetOwnerName("ReconcileTest");
        dbClient.Save();

        await using (var session = Integration.GetNeo4jDriver().AsyncSession())
        {
            await session.ExecuteWriteAsync(async tx =>
                await tx.RunAsync("MATCH (:User {userId:$userId})-[r:OWES {debtId:$debtId}]->() DELETE r",
                    new { userId = $"{user.ShardName}_{user.Id}", debtId = debt.Id }));
        }

        var reconciliationService = Integration.ServiceProvider.GetRequiredService<GraphReconciliationService>();
        await reconciliationService.ReconcileAsync(CancellationToken.None);

        await using var verifySession = Integration.GetNeo4jDriver().AsyncSession();
        var result = await verifySession.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("MATCH (:User {userId:$userId})-[r:OWES]->(o:DebtOwner {name:$name}) RETURN count(r) AS cnt",
                new { userId = $"{user.ShardName}_{user.Id}", name = "ReconcileTest" });

            return await cursor.SingleAsync();
        });

        Assert.That(result["cnt"].As<int>(), Is.GreaterThanOrEqualTo(1));
    }
}
