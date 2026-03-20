using System.Net;
using System.Text;
using System.Text.Json;

namespace Money.Api.Tests.TestTools;


public class MockCubeHttpHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);

        if (request.RequestUri?.PathAndQuery.Contains("/meta") == true)
        {
            return Task.FromResult(CreateMetaResponse());
        }

        return Task.FromResult(CreateLoadResponse());
    }

    private static HttpResponseMessage CreateLoadResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new Dictionary<string, object>
                {
                    ["operations.category_name"] = "Еда",
                    ["operations.total_sum"] = 15000.00,
                    ["operations.count"] = 10,
                },
            },
        });

        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage CreateMetaResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            cubes = new[]
            {
                new
                {
                    name = "operations",
                    measures = new[] { new { name = "operations.total_sum", title = "Total Sum", shortTitle = "Total Sum", type = "sum" } },
                    dimensions = new[] { new { name = "operations.category_name", title = "Category", shortTitle = "Category", type = "string" } },
                },
                new
                {
                    name = "debts",
                    measures = new[] { new { name = "debts.total_debt", title = "Total Debt", shortTitle = "Total Debt", type = "sum" } },
                    dimensions = new[] { new { name = "debts.owner_name", title = "Owner", shortTitle = "Owner", type = "string" } },
                },
            },
        });

        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
