namespace Money.Business.Interfaces;

public interface ICounterCacheService
{
    Task<int?> IncrementAsync(string shardName, int userId, string entityType, CancellationToken cancellationToken = default);
    Task SetAsync(string shardName, int userId, string entityType, int value, CancellationToken cancellationToken = default);
}
