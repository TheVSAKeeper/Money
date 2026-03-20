using Microsoft.Extensions.DependencyInjection;
using Money.ApiClient;
using System.IdentityModel.Tokens.Jwt;

namespace Money.Api.Tests.Tests.Analytics;

public class CubeApiTests
{
    private DatabaseClient _dbClient = null!;
    private TestUser _user = null!;
    private MoneyClient _apiClient = null!;
#pragma warning disable NUnit1032
    private MockCubeHttpHandler _mockHandler = null!;
#pragma warning restore NUnit1032

    [SetUp]
    public void Setup()
    {
        _dbClient = Integration.GetDatabaseClient();
        _user = _dbClient.WithUser();
        _dbClient.Save();

        _apiClient = new(Integration.GetHttpClient(), Console.WriteLine);
        _apiClient.SetUser(_user);

        _mockHandler = Integration.ServiceProvider.GetRequiredService<MockCubeHttpHandler>();
        _mockHandler.Requests.Clear();
    }

    [Test]
    public async Task CubeApi_Debts_ReturnsData()
    {
        var result = await _apiClient.Admin.GetCubeDebts();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessStatusCode, Is.True);
            Assert.That(result.Content, Is.Not.Null);
        }
    }

    [Test]
    public async Task CubeApi_Expenses_RequiresAuthentication()
    {
        var unauthClient = new MoneyClient(Integration.GetHttpClient(), Console.WriteLine);

        var result = await unauthClient.Admin.GetCubeExpenses();

        Assert.That(result.IsSuccessStatusCode, Is.False);
    }

    [Test]
    public async Task CubeApi_Expenses_ReturnsAggregatedByCategory()
    {
        var result = await _apiClient.Admin.GetCubeExpenses(period: "last month");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessStatusCode, Is.True);
            Assert.That(result.Content!.Data, Is.Not.Empty);
            Assert.That(result.Content.Data.First().Keys, Does.Contain("operations.category_name"));
        }
    }

    [Test]
    public async Task CubeApi_Meta_ReturnsCubeDefinitions()
    {
        var result = await _apiClient.Admin.GetCubeMeta();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessStatusCode, Is.True);
            Assert.That(result.Content!.Cubes, Is.Not.Empty);
            Assert.That(result.Content.Cubes.Select(c => c.Name), Does.Contain("operations"));
        }
    }

    [Test]
    public async Task CubeApi_SecurityContext_JwtContainsUserId()
    {
        await _apiClient.Admin.GetCubeExpenses();

        var requests = _mockHandler.Requests;
        Assert.That(requests, Is.Not.Empty);

        var jwt = requests.Last().Headers.Authorization?.Parameter;
        Assert.That(jwt, Is.Not.Null);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        var userIdClaim = token.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
        Assert.That(userIdClaim, Is.EqualTo(_user.Id.ToString()));
    }

    [Test]
    public async Task CubeApi_Trends_ReturnsData()
    {
        var result = await _apiClient.Admin.GetCubeTrends(granularity: "month");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessStatusCode, Is.True);
            Assert.That(result.Content, Is.Not.Null);
        }
    }
}
