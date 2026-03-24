using Neo4j.Driver;

namespace Money.Data.Graph;

public class Neo4jSchemaInitializer(IDriver driver)
{
    public async Task EnsureSchemaAsync()
    {
        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CREATE CONSTRAINT user_userId IF NOT EXISTS FOR (u:User) REQUIRE u.userId IS UNIQUE");

            await tx.RunAsync("CREATE INDEX debtowner_user_name IF NOT EXISTS FOR (n:DebtOwner) ON (n.userId, n.name)");

            await tx.RunAsync("CREATE INDEX category_user_id IF NOT EXISTS FOR (n:Category) ON (n.userId, n.id)");

            await tx.RunAsync("CREATE INDEX month_value IF NOT EXISTS FOR (m:Month) ON (m.value)");
        });
    }
}
