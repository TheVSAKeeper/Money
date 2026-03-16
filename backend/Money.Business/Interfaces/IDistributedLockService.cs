namespace Money.Business.Interfaces;

public interface IDistributedLockService
{
    Task<IAsyncDisposable> AcquireAsync(
        string key,
        TimeSpan ttl,
        int retryCount = 3,
        CancellationToken cancellationToken = default);
}
