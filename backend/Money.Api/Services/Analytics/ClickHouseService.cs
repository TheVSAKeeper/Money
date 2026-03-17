using ClickHouse.Driver.ADO;
using System.Data.Common;
using System.Dynamic;
using System.Globalization;
using System.Text;

namespace Money.Api.Services.Analytics;

public class ClickHouseService(ClickHouseDataSource dataSource)
{
    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<T>> QueryScalarAsync<T>(string sql, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<T>();

        while (await reader.ReadAsync(ct))
        {
            var raw = reader.GetValue(0);
            results.Add((T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture));
        }

        return results;
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> mapper, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<T>();

        while (await reader.ReadAsync(ct))
        {
            results.Add(mapper(reader));
        }

        return results;
    }

    public async Task<List<dynamic>> QueryDynamicAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<dynamic>();

        while (await reader.ReadAsync(ct))
        {
            IDictionary<string, object?> expando = new ExpandoObject();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                expando[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            results.Add((dynamic)expando);
        }

        return results;
    }

    public async Task InsertBatchAsync(string insertSpec, IEnumerable<object[]> rows, CancellationToken ct = default)
    {
        var rowList = rows.ToList();

        if (rowList.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder("INSERT INTO ");
        sb.Append(insertSpec);
        sb.Append(" VALUES ");

        for (var i = 0; i < rowList.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append('(');
            sb.AppendJoin(", ", rowList[i].Select(FormatValue));
            sb.Append(')');
        }

        await ExecuteAsync(sb.ToString(), ct);
    }

    private static string FormatValue(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "NULL";
        }

        return value switch
        {
            string s => $"'{EscapeString(s)}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            double dbl => dbl.ToString(CultureInfo.InvariantCulture),
            float flt => flt.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            _ => value.ToString() ?? "NULL",
        };
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\0", "\\0")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
