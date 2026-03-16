namespace Money.Business.Interfaces;

public interface IOperationCacheService
{
    Task<List<Operation>?> GetAsync(string shardName, int userId, OperationFilter filter, CancellationToken cancellationToken = default);
    Task SetAsync(string shardName, int userId, OperationFilter filter, List<Operation> data, CancellationToken cancellationToken = default);
    Task InvalidateAllForUserAsync(string shardName, int userId, CancellationToken cancellationToken = default);
}
