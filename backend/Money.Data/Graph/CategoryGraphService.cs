using Neo4j.Driver;

namespace Money.Data.Graph;

public sealed class CategoryGraphService(Neo4jSessionFactory sessionFactory) : ICategoryGraphService
{
    public async Task SyncCategoryAsync(string userId, int categoryId, string name, int typeId, int? parentId)
    {
        await sessionFactory.ExecuteWriteAsync("""
                                               MERGE (c:Category {userId: $userId, id: $categoryId})
                                               SET c.name = $name, c.type = $typeId
                                               WITH c
                                               OPTIONAL MATCH (c)-[old:SUBCATEGORY_OF]->()
                                               DELETE old
                                               """, new { userId, categoryId, name, typeId });

        if (parentId != null)
        {
            await sessionFactory.ExecuteWriteAsync("""
                                                   MATCH (c:Category {userId: $userId, id: $categoryId})
                                                   MERGE (p:Category {userId: $userId, id: $parentId})
                                                   MERGE (c)-[:SUBCATEGORY_OF]->(p)
                                                   """, new { userId, categoryId, parentId });
        }
    }

    public Task DeleteCategoryAsync(string userId, int categoryId)
    {
        return sessionFactory.ExecuteWriteAsync("""
                                                MATCH (c:Category {userId: $userId, id: $categoryId})
                                                DETACH DELETE c
                                                """, new { userId, categoryId });
    }

    public Task UpdateOperationFlowAsync(string userId, int categoryId, string yearMonth, long sumDeltaCents, int countDelta)
    {
        return sessionFactory.ExecuteWriteAsync("""
                                                MERGE (c:Category {userId: $userId, id: $categoryId})
                                                MERGE (m:Month {value: $yearMonth})
                                                MERGE (c)-[r:HAS_OPERATIONS]->(m)
                                                SET r.totalSumCents = coalesce(r.totalSumCents, 0) + $sumDeltaCents,
                                                    r.count = coalesce(r.count, 0) + $countDelta
                                                """, new { userId, categoryId, yearMonth, sumDeltaCents, countDelta });
    }

    public async Task<GraphDto> GetCategoryTreeAsync(string userId, int limit = 500)
    {
        var records = await sessionFactory.ExecuteReadAsync("""
                                                            MATCH (c:Category {userId: $userId})
                                                            OPTIONAL MATCH (c)-[sub:SUBCATEGORY_OF]->(p:Category)
                                                            OPTIONAL MATCH (c)-[ops:HAS_OPERATIONS]->(m:Month)
                                                            RETURN c, sub, p, ops, m
                                                            LIMIT $limit
                                                            """, new { userId, limit });

        var nodes = new Dictionary<string, NodeDto>();
        var edges = new List<EdgeDto>();

        foreach (var record in records)
        {
            var catNode = record["c"].As<INode>();
            var catId = $"cat_{catNode["id"].As<int>()}";

            nodes.TryAdd(catId, new(catId, catNode["name"].As<string>(), new(catNode.Properties)));

            if (record["sub"].As<IRelationship?>() is { } subRel)
            {
                var parentNode = record["p"].As<INode>();
                var parentId = $"cat_{parentNode["id"].As<int>()}";

                nodes.TryAdd(parentId, new(parentId, parentNode["name"].As<string>(), new(parentNode.Properties)));
                edges.Add(new(catId, parentId, "SUBCATEGORY_OF", new(subRel.Properties)));
            }

            if (record["ops"].As<IRelationship?>() is { } opsRel)
            {
                var monthNode = record["m"].As<INode>();
                var monthId = $"month_{monthNode["value"].As<string>()}";

                nodes.TryAdd(monthId, new(monthId, monthNode["value"].As<string>(), new(monthNode.Properties)));
                edges.Add(new(catId, monthId, "HAS_OPERATIONS", new(opsRel.Properties)));
            }
        }

        return new(nodes.Values.ToList(), edges);
    }
}
