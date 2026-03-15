namespace Money.Api.Dto.Admin;

/// <summary>
/// Состояние партиций таблицы operations на одном шарде.
/// </summary>
/// <param name="Shard">Имя шарда.</param>
/// <param name="LastMaintenanceUtc">Время последнего успешного обслуживания партиций.</param>
/// <param name="Partitions">Список партиций.</param>
public sealed record PartitionListResponse(string Shard, DateTimeOffset LastMaintenanceUtc, List<PartitionInfo> Partitions);

/// <summary>
/// Информация об одной месячной партиции.
/// </summary>
/// <param name="Name">Имя таблицы-партиции (например, operations_2026_03).</param>
/// <param name="ApproxRows">Приблизительное количество строк из pg_stat_user_tables.n_live_tup.</param>
/// <param name="SizeBytes">Полный размер партиции в байтах (включая индексы).</param>
/// <param name="RangeStart">Начало диапазона дат (включительно).</param>
/// <param name="RangeEnd">Конец диапазона дат (не включая).</param>
public sealed record PartitionInfo(string Name, long ApproxRows, long SizeBytes, DateOnly RangeStart, DateOnly RangeEnd);
