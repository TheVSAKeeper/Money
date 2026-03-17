using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.Api.Services.Analytics;
using Money.ApiClient;

namespace Money.Api.Tests.Tests.ClickHouse;

public class OperationSyncTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
    private ClickHouseService _chService = null!;
#pragma warning disable NUnit1032
    private ClickHouseSyncService _syncService = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);

        _chService = Integration.ServiceProvider.GetRequiredService<ClickHouseService>();
        _syncService = Integration.ServiceProvider.GetRequiredService<ClickHouseSyncService>();
    }

    [Test]
    public async Task ClickHouse_AggregationMatchesPostgreSQL()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var sums = new[] { 100m, 200m, 300m };

        foreach (var sum in sums)
        {
            await _apiClient.Operations.Create(new()
                {
                    CategoryId = category.Id,
                    Sum = sum,
                    Date = DateTime.Today,
                })
                .IsSuccessWithContent();
        }

        await _syncService.RunSyncAsync();

        var chSum = await _chService.QueryScalarAsync<decimal>($"SELECT sum(sum) FROM operations_analytics WHERE user_id = {_user.Id} AND category_id = {category.Id}");

        Assert.That(chSum.First(), Is.EqualTo(sums.Sum()));
    }

    [Test]
    public async Task Debt_SyncedToClickHouse_AfterCreate()
    {
        _dbClient.Save();

        await _apiClient.Debts.Create(new()
            {
                OwnerName = "Test Owner",
                Sum = 500,
                Date = DateTime.Today,
                TypeId = 1,
            })
            .IsSuccessWithContent();

        await _syncService.RunSyncAsync();

        var count = await _chService.QueryScalarAsync<long>($"SELECT count() FROM debts_analytics FINAL WHERE user_id = {_user.Id}");

        Assert.That(count.First(), Is.GreaterThan(0));
    }

    [Test]
    public async Task Operation_SyncedToClickHouse_AfterCreate()
    {
        var category = _user.WithCategory();
        _dbClient.Save();

        var request = new OperationsClient.SaveRequest
        {
            CategoryId = category.Id,
            Sum = 100,
            Date = DateTime.Today,
        };

        await _apiClient.Operations.Create(request).IsSuccessWithContent();

        await _syncService.RunSyncAsync();

        var count = await _chService.QueryScalarAsync<long>($"SELECT count() FROM operations_analytics WHERE user_id = {_user.Id}");

        Assert.That(count.First(), Is.GreaterThan(0));
    }
}
