using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Money.Api.Services.Analytics;

public class CubeApiService(HttpClient httpClient, CubeSettings settings, ILogger<CubeApiService> logger)
{
    private const int MaxRetries = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<CubeResultSet> QueryAsync(CubeQuery query, int userId, CancellationToken ct = default)
    {
        return ExecuteLoadAsync(query, userId, ct);
    }

    public Task<CubeResultSet> GetExpenseCubeAsync(
        int userId,
        DateOnly from,
        DateOnly to,
        string[] dimensions,
        string granularity,
        CancellationToken ct = default)
    {
        var query = new CubeQuery
        {
            Measures = ["operations.total_sum", "operations.count"],
            Dimensions = dimensions.Select(d => $"operations.{d}").ToArray(),
            TimeDimensions =
            [
                new("operations.date", granularity, [from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd")]),
            ],
            Limit = 500,
        };

        return ExecuteLoadAsync(query, userId, ct);
    }

    public Task<CubeResultSet> GetDebtCubeAsync(
        int userId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        string[] dateRange = from.HasValue && to.HasValue
            ? [from.Value.ToString("yyyy-MM-dd"), to.Value.ToString("yyyy-MM-dd")]
            : ["last 12 months"];

        var query = new CubeQuery
        {
            Measures = ["debts.total_debt", "debts.total_paid", "debts.outstanding", "debts.count"],
            Dimensions = ["debts.owner_name", "debts.debt_type", "debts.status"],
            TimeDimensions = [new("debts.date", "month", dateRange)],
            Limit = 500,
        };

        return ExecuteLoadAsync(query, userId, ct);
    }

    public Task<CubeResultSet> GetTrendCubeAsync(
        int userId,
        string granularity,
        string dateRange,
        CancellationToken ct = default)
    {
        var query = new CubeQuery
        {
            Measures = ["operations.total_sum", "operations.net_balance"],
            Dimensions = ["operations.operation_type"],
            TimeDimensions = [new("operations.date", granularity, [dateRange])],
            Limit = 500,
        };

        return ExecuteLoadAsync(query, userId, ct);
    }

    public Task<CubeResultSet> GetApiMetricsAsync(string dateRange, CancellationToken ct = default)
    {
        var query = new CubeQuery
        {
            Measures = ["api_metrics.count", "api_metrics.avg_duration", "api_metrics.error_rate"],
            Dimensions = ["api_metrics.endpoint", "api_metrics.method"],
            TimeDimensions = [new("api_metrics.timestamp", "hour", [dateRange])],
            Limit = 100,
        };

        return ExecuteLoadAsync(query, null, ct);
    }

    public async Task<CubeMeta> GetMetaAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/cubejs-api/v1/meta");
        request.Headers.Authorization = new("Bearer", GenerateJwt(null));

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<CubeMetaResponse>(JsonOptions, ct);

        return new()
        {
            Cubes = body?.Cubes?.Select(c => new CubeDef(c.Name ?? "",
                            c.Measures?.Select(m => new CubeMemberDef(m.Name ?? "", m.Title ?? "", m.ShortTitle ?? "", m.Type ?? "")).ToList() ?? [],
                            c.Dimensions?.Select(d => new CubeMemberDef(d.Name ?? "", d.Title ?? "", d.ShortTitle ?? "", d.Type ?? "")).ToList() ?? []))
                        .ToList()
                    ?? [],
        };
    }

    internal string GenerateJwt(int? userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.ApiSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>();

        if (userId.HasValue)
        {
            claims.Add(new("userId", userId.Value.ToString()));
        }

        var token = new JwtSecurityToken(claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static object BuildLoadRequest(CubeQuery query)
    {
        return new
        {
            query = new
            {
                measures = query.Measures,
                dimensions = query.Dimensions,
                filters = query.Filters.Select(f => new { member = f.Member, @operator = f.Operator, values = f.Values }),
                timeDimensions = query.TimeDimensions.Select(td => new
                {
                    dimension = td.Dimension,
                    granularity = td.Granularity,
                    dateRange = td.DateRange.Length == 1 ? (object)td.DateRange[0] : td.DateRange,
                }),
                limit = query.Limit > 0 ? (int?)query.Limit : null,
            },
        };
    }

    private static CubeResultSet MapToResultSet(CubeLoadResponse? body)
    {
        return new()
        {
            Data = body?.Data ?? [],
            Annotation = body?.Annotation,
        };
    }

    private async Task<CubeResultSet> ExecuteLoadAsync(CubeQuery query, int? userId, CancellationToken ct)
    {
        var jwt = GenerateJwt(userId);
        var requestBody = BuildLoadRequest(query);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/cubejs-api/v1/load");
            request.Headers.Authorization = new("Bearer", jwt);
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<CubeLoadResponse>(JsonOptions, ct);

            if (body?.Error != "Continue wait")
            {
                return MapToResultSet(body);
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            logger.LogInformation("Cube.dev pre-aggregation building, retry {Attempt}/{MaxRetries} in {Delay}s",
                attempt + 1, MaxRetries, delay.TotalSeconds);

            await Task.Delay(delay, ct);
        }

        throw new TimeoutException("Cube.dev pre-aggregation did not complete within retry limit");
    }

    // Internal response types for JSON deserialization
    private sealed class CubeLoadResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("data")]
        public List<Dictionary<string, object?>>? Data { get; set; }

        [JsonPropertyName("annotation")]
        public Dictionary<string, CubeAnnotation>? Annotation { get; set; }
    }

    private sealed class CubeMetaResponse
    {
        [JsonPropertyName("cubes")]
        public List<CubeDefRaw>? Cubes { get; set; }
    }

    private sealed class CubeDefRaw
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("measures")]
        public List<CubeMemberRaw>? Measures { get; set; }

        [JsonPropertyName("dimensions")]
        public List<CubeMemberRaw>? Dimensions { get; set; }
    }

    private sealed class CubeMemberRaw
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("shortTitle")]
        public string? ShortTitle { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}
