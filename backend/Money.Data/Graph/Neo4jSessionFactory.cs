using Neo4j.Driver;
using System.Reflection;

namespace Money.Data.Graph;

public class Neo4jSessionFactory(IDriver driver)
{
    public static Dictionary<string, object?> ToDictionary(object parameters)
    {
        if (parameters is Dictionary<string, object?> dict)
        {
            return new(dict);
        }

        return parameters.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.GetValue(parameters));
    }

    public IAsyncSession OpenSession()
    {
        return driver.AsyncSession();
    }

    public async Task ExecuteWriteAsync(string cypher, object parameters)
    {
        var dict = ToDictionary(parameters);

        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
            await tx.RunAsync(cypher, dict));
    }

    public async Task<List<IRecord>> ExecuteReadAsync(string cypher, object? parameters = null)
    {
        var dict = parameters != null
            ? ToDictionary(parameters)
            : new();

        await using var session = driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, dict);
            return await cursor.ToListAsync();
        });
    }
}
