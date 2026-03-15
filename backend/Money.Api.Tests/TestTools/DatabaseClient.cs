using Money.ApiClient;
using Money.Data;
using Money.Data.Sharding;

namespace Money.Api.Tests.TestTools;

public sealed class DatabaseClient(
    ShardedDbContextFactory shardFactory,
    ShardRouter shardRouter,
    Func<RoutingDbContext> createRoutingDbContext,
    MoneyClient apiClient)
{
    private static readonly Lock LockObject = new();

    private List<TestObject>? _testObjects = [];
    private ApplicationDbContext? _context;
    private string? _shardName;

    public ShardedDbContextFactory ShardFactory { get; } = shardFactory;
    public ShardRouter ShardRouter { get; } = shardRouter;
    public Func<RoutingDbContext> CreateRoutingDbContext { get; } = createRoutingDbContext;
    public MoneyClient ApiClient { get; } = apiClient;

    public ApplicationDbContext Context => _context ??= CreateApplicationDbContext();

    public ApplicationDbContext CreateApplicationDbContext()
    {
        return ShardFactory.Create(_shardName ?? throw new InvalidOperationException("Шард не определён. Сначала зарегистрируйте пользователя."));
    }

    public void SetShard(string shardName)
    {
        if (_shardName == shardName)
        {
            return;
        }

        _context?.Dispose();
        _context = null;
        _shardName = shardName;
    }

    public TestUser WithUser()
    {
        var obj = new TestUser();
        obj.Attach(this);
        return obj;
    }

    public void AddObject(TestObject testObject)
    {
        _testObjects?.Add(testObject);
    }

    public DatabaseClient Save()
    {
        if (_testObjects != null)
        {
            // поскольку тесты в несколько потоков это выполняют, а политика партии пока не рассматривает конкаранси случаи
            lock (LockObject)
            {
                foreach (var testObject in _testObjects)
                {
                    testObject.SaveObject();
                }
            }
        }

        return this;
    }

    public DatabaseClient Clear()
    {
        lock (LockObject)
        {
            _testObjects = [];
        }

        return this;
    }
}
