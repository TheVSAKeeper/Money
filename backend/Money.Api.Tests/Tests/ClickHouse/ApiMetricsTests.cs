using Microsoft.Extensions.DependencyInjection;
using Money.Api.BackgroundServices;
using Money.Api.Services.Analytics;
using Money.ApiClient;

namespace Money.Api.Tests.Tests.ClickHouse;

public class ApiMetricsTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
    private ClickHouseService _chService = null!;
#pragma warning disable NUnit1032
    private ClickHouseMetricsService _metricsService = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);

        _chService = Integration.ServiceProvider.GetRequiredService<ClickHouseService>();
        _metricsService = Integration.ServiceProvider.GetRequiredService<ClickHouseMetricsService>();
    }

    [Test]
    public async Task ApiMetrics_RecordedForEachRequest()
    {
        _dbClient.Save();

        await _apiClient.Categories.Get().IsSuccess();

        await _metricsService.RunFlushAsync();

        var metrics = await _chService.QueryAsync("SELECT timestamp, user_id, endpoint, method, status_code, duration_ms FROM api_metrics WHERE lower(endpoint) LIKE '%categor%' ORDER BY timestamp DESC LIMIT 1",
            r => new ApiMetric
            {
                Timestamp = r.GetDateTime(0),
                UserId = r.IsDBNull(1) ? null : r.GetInt32(1),
                Endpoint = r.GetString(2),
                Method = r.GetString(3),
                StatusCode = r.GetInt32(4),
                DurationMs = r.GetDouble(5),
            });

        Assert.That(metrics, Is.Not.Empty);
        Assert.That(metrics.First().StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task ApiMetrics_TTL_FieldDefined()
    {
        var table = await _chService.QueryDynamicAsync("SELECT engine_full FROM system.tables WHERE name = 'api_metrics' AND database = 'default'");

        Assert.That(table, Is.Not.Empty);
        Assert.That(table.First().engine_full?.ToString(), Does.Contain("TTL"));
    }

    [Test]
    public async Task ApiMetrics_UsesRouteTemplate_NotConcreteUrl()
    {
        _dbClient.Save();
        var category = _user.WithCategory();
        _dbClient.Save();

        await _apiClient.Categories.GetById(category.Id).IsSuccess();
        await _metricsService.RunFlushAsync();

        var endpoints = await _chService.QueryAsync("SELECT endpoint FROM api_metrics ORDER BY timestamp DESC LIMIT 1",
            r => r.GetString(0));

        Assert.That(endpoints.First(), Does.Not.Contain(category.Id.ToString()));
    }
}
