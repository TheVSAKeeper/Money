namespace Money.Business.Interfaces;

public interface ICategoryCacheService
{
    Task<List<Category>?> GetAsync(string shardName, int userId, CancellationToken cancellationToken = default);
    Task SetAsync(string shardName, int userId, List<Category> data, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string shardName, int userId, CancellationToken cancellationToken = default);
}
