using Microsoft.Extensions.Logging.Abstractions;
using Money.Api.Services.Analytics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Money.Api.Tests.Tests.Analytics;

[TestFixture]
public class CubeApiServiceTests
{
    [SetUp]
    public void Setup()
    {
        _settings = new(new("http://cube-mock:4000"), TestSecret);
    }

    private const string TestSecret = "test-cube-secret-1234567890-must-be-long-enough";
    private CubeSettings _settings = null!;

    [Test]
    public void GenerateJwt_ContainsUserId()
    {
        var service = CreateService(new AlwaysOkHandler());

        var jwt = service.GenerateJwt(42);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var userIdClaim = token.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;

        Assert.That(userIdClaim, Is.EqualTo("42"));
    }

    [Test]
    public void GenerateJwt_ExpiresIn5Minutes()
    {
        var service = CreateService(new AlwaysOkHandler());

        var before = DateTime.UtcNow;
        var jwt = service.GenerateJwt(1);
        var after = DateTime.UtcNow;

        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);

        Assert.That(token.ValidTo, Is.GreaterThan(before.AddMinutes(4)));
        Assert.That(token.ValidTo, Is.LessThan(after.AddMinutes(6)));
    }

    [Test]
    public void GenerateJwt_SystemMetrics_NoUserIdClaim()
    {
        var service = CreateService(new AlwaysOkHandler());

        var jwt = service.GenerateJwt(null);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        var userIdClaim = token.Claims.FirstOrDefault(c => c.Type == "userId");

        Assert.That(userIdClaim, Is.Null);
    }

    [Test]
    public async Task QueryAsync_OnContinueWait_RetriesWithBackoff()
    {
        var handler = new ContinueWaitThenOkHandler(failCount: 2);
        var service = CreateService(handler);

        var result = await service.QueryAsync(new()
        {
            Measures = ["operations.total_sum"],
        }, 1);

        Assert.That(handler.CallCount, Is.EqualTo(3));
        Assert.That(result.Data, Is.Not.Empty);
    }

    [Test]
    public void QueryAsync_OnContinueWait_ThrowsWhenCancelled()
    {
        var handler = new AlwaysContinueWaitHandler();
        var service = CreateService(handler);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));

        Assert.ThrowsAsync<TaskCanceledException>(() => service.QueryAsync(new()
        {
            Measures = ["operations.total_sum"],
        }, 1, cts.Token));
    }

    private CubeApiService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = _settings.BaseUrl };
        return new(httpClient, _settings, NullLogger<CubeApiService>.Instance);
    }

    [Test]
    public void QueryAsync_On400Error_IncludesResponseBodyInException()
    {
        const string ErrorBody = "{\"error\":\"Unknown member: bogus.field\"}";
        var handler = new BadRequestHandler(ErrorBody);
        var service = CreateService(handler);

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => service.QueryAsync(new()
        {
            Measures = ["operations.total_sum"],
        }, 1));

        Assert.That(ex!.Message, Does.Contain("bogus.field"));
    }

    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(new { data = new[] { new Dictionary<string, object> { ["x"] = 1 } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ContinueWaitThenOkHandler(int failCount) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;

            if (CallCount <= failCount)
            {
                var wait = JsonSerializer.Serialize(new { error = "Continue wait" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wait, Encoding.UTF8, "application/json"),
                });
            }

            var ok = JsonSerializer.Serialize(new { data = new[] { new Dictionary<string, object> { ["x"] = 1 } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ok, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class BadRequestHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class AlwaysContinueWaitHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(new { error = "Continue wait" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
