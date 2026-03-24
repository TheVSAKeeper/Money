using Neo4j.Driver;

namespace Money.Data.Graph;

public class DebtGraphService(Neo4jSessionFactory sessionFactory) : IDebtGraphService
{
    public static long ToNeo4jAmount(decimal amount)
    {
        return (long)(amount * 100);
    }

    public async Task SyncDebtAsync(string userId, int debtId, string ownerName, decimal amount, string status, int type)
    {
        await sessionFactory.ExecuteWriteAsync("""
                                               MERGE (u:User {userId: $userId})
                                               MERGE (o:DebtOwner {userId: $userId, name: $ownerName})
                                               MERGE (u)-[r:OWES {debtId: $debtId}]->(o)
                                               SET r.amountCents = $amountCents,
                                                   r.status = $status, r.type = $type
                                               """, new { userId, debtId, ownerName, amountCents = ToNeo4jAmount(amount), status, type });
    }

    public async Task SyncPaymentAsync(string userId, int debtId, decimal paySum, string newStatus)
    {
        await sessionFactory.ExecuteWriteAsync("""
                                               MATCH (:User {userId: $userId})-[r:OWES {debtId: $debtId}]->()
                                               SET r.paySumCents = $paySumCents, r.status = $newStatus
                                               """, new { userId, debtId, paySumCents = ToNeo4jAmount(paySum), newStatus });
    }

    public async Task ForgiveDebtAsync(string userId, int debtId)
    {
        await sessionFactory.ExecuteWriteAsync("""
                                               MATCH (:User {userId: $userId})-[r:OWES {debtId: $debtId}]->()
                                               SET r.status = 'forgiven'
                                               """, new { userId, debtId });
    }

    public async Task DeleteDebtAsync(string userId, int debtId)
    {
        await sessionFactory.ExecuteWriteAsync("""
                                               MATCH (:User {userId: $userId})-[r:OWES {debtId: $debtId}]->()
                                               DELETE r
                                               """, new { userId, debtId });
    }

    public async Task MergeOwnersAsync(string userId, string fromName, string toName)
    {
        await sessionFactory.ExecuteWriteAsync("""
                                               MATCH (u:User {userId: $userId})-[r:OWES]->(from:DebtOwner {userId: $userId, name: $fromName})
                                               MERGE (to:DebtOwner {userId: $userId, name: $toName})
                                               CREATE (u)-[nr:OWES]->(to)
                                               SET nr = properties(r)
                                               DELETE r
                                               WITH from
                                               WHERE NOT EXISTS { (from)--() }
                                               DELETE from
                                               """, new { userId, fromName, toName });
    }

    public async Task<GraphDto> GetDebtNetworkAsync(string userId, int limit = 200)
    {
        var records = await sessionFactory.ExecuteReadAsync("""
                                                            MATCH (u:User {userId: $userId})-[r:OWES]->(o:DebtOwner)
                                                            RETURN u, r, o
                                                            LIMIT $limit
                                                            """, new { userId, limit });

        var nodes = new Dictionary<string, NodeDto>();
        var edges = new List<EdgeDto>();

        foreach (var record in records)
        {
            var userNode = record["u"].As<INode>();
            var ownerNode = record["o"].As<INode>();
            var rel = record["r"].As<IRelationship>();

            var userNodeId = $"user_{userNode["userId"].As<string>()}";
            var ownerNodeId = $"owner_{ownerNode["name"].As<string>()}";

            nodes.TryAdd(userNodeId, new(userNodeId, "User", new(userNode.Properties)));
            nodes.TryAdd(ownerNodeId, new(ownerNodeId, ownerNode["name"].As<string>(), new(ownerNode.Properties)));

            edges.Add(new(userNodeId, ownerNodeId, "OWES", new(rel.Properties)));
        }

        return new(nodes.Values.ToList(), edges);
    }
}
