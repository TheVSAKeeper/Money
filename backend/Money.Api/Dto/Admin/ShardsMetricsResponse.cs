namespace Money.Api.Dto.Admin;

/// <summary>
/// Ответ с метриками шардов.
/// </summary>
public class ShardsMetricsResponse
{
    /// <summary>
    /// Метрики по каждому шарду.
    /// </summary>
    public Dictionary<string, ShardMetrics> Shards { get; init; } = new();

    /// <summary>
    /// Имя шарда текущего пользователя.
    /// </summary>
    public required string CurrentUserShard { get; init; }
}

/// <summary>
/// Метрики одного шарда.
/// </summary>
public class ShardMetrics
{
    /// <summary>
    /// Таблицы в шарде.
    /// </summary>
    public List<TableMetrics> Tables { get; init; } = [];

    /// <summary>
    /// Общее количество строк.
    /// </summary>
    public long TotalRows { get; init; }

    /// <summary>
    /// Суммарный размер таблиц в байтах.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Размер базы данных в байтах.
    /// </summary>
    public long DbSizeBytes { get; init; }
}

/// <summary>
/// Метрики таблицы в шарде.
/// </summary>
public class TableMetrics
{
    /// <summary>
    /// Имя таблицы.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Количество «живых» строк.
    /// </summary>
    public long LiveRows { get; init; }

    /// <summary>
    /// Количество «мёртвых» строк (для VACUUM).
    /// </summary>
    public long DeadRows { get; init; }

    /// <summary>
    /// Размер таблицы в байтах.
    /// </summary>
    public long SizeBytes { get; init; }
}
