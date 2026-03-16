using Money.Business.Interfaces;
using Money.Data.Entities;

namespace Money.Business.Services;

public class UsersService(
    RequestEnvironment environment,
    ApplicationDbContext context,
    ICounterCacheService counterCache)
{
    private DomainUser? _currentUser;

    public async Task<int> GetIdAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentAsync(cancellationToken);
        return user.Id;
    }

    public Task<int> GetNextCategoryIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("category", x => x.NextCategoryId, x => x.NextCategoryId++, cancellationToken);
    }

    public Task<int> GetNextOperationIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("operation", x => x.NextOperationId, x => x.NextOperationId++, cancellationToken);
    }

    public Task<int> GetNextPlaceIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("place", x => x.NextPlaceId, x => x.NextPlaceId++, cancellationToken);
    }

    public Task<int> GetNextFastOperationIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("fastoperation", x => x.NextFastOperationId, x => x.NextFastOperationId++, cancellationToken);
    }

    public Task<int> GetNextRegularOperationIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("regularoperation", x => x.NextRegularOperationId, x => x.NextRegularOperationId++, cancellationToken);
    }

    public Task<int> GetNextDebtIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("debt", x => x.NextDebtId, x => x.NextDebtId++, cancellationToken);
    }

    public Task<int> GetNextDebtOwnerIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("debtowner", x => x.NextDebtOwnerId, x => x.NextDebtOwnerId++, cancellationToken);
    }

    public Task<int> GetNextCarIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("car", x => x.NextCarId, x => x.NextCarId++, cancellationToken);
    }

    public Task<int> GetNextCarEventIdAsync(CancellationToken cancellationToken = default)
    {
        return GetNextIdAsync("carevent", x => x.NextCarEventId, x => x.NextCarEventId++, cancellationToken);
    }

    public async Task SetNextCategoryIdAsync(int index, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentAsync(cancellationToken);
        user.NextCategoryId = index;

        if (environment.ShardName != null)
        {
            await counterCache.SetAsync(environment.ShardName, user.Id, "category", index, cancellationToken);
        }
    }

    private async Task<DomainUser> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        // TODO: контекст и так кэширует полученные данные, а он у нас один на реквест как и сервис
        return _currentUser ??= await context.DomainUsers.FirstOrDefaultAsync(x => x.Id == environment.UserId, cancellationToken)
                                ?? throw new BusinessException("Извините, но пользователь не найден.");
    }

    private async Task<int> GetNextIdAsync(
        string entityType,
        Func<DomainUser, int> getId,
        Action<DomainUser> incrementId,
        CancellationToken cancellationToken)
    {
        if (environment.ShardName != null)
        {
            var redisId = await counterCache.IncrementAsync(environment.ShardName, environment.UserId, entityType, cancellationToken);

            if (redisId.HasValue)
            {
                return redisId.Value;
            }
        }

        var user = await GetCurrentAsync(cancellationToken);
        var id = getId(user);
        incrementId(user);
        return id;
    }
}
