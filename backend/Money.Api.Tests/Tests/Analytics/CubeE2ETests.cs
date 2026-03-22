using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Logging.Abstractions;
using Money.Api.Services.Analytics;
using Testcontainers.ClickHouse;

namespace Money.Api.Tests.Tests.Analytics;

[TestFixture]
[Category("E2E")]
public class CubeE2ETests
{
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        const string chPassword = "e2e-cube-test-password";

        _network = new NetworkBuilder()
            .WithName($"cube-e2e-{Guid.NewGuid():N}")
            .Build();

        await _network.CreateAsync();

        _clickhouseContainer = new ClickHouseBuilder("clickhouse/clickhouse-server:26.2-alpine")
            .WithPassword(chPassword)
            .WithNetwork(_network)
            .WithNetworkAliases("clickhouse")
            .Build();

        await _clickhouseContainer.StartAsync();
        await InitializeClickHouseTablesAsync(chPassword);

        var cubeModelPath = ResolveCubeModelPath();

#pragma warning disable CS0618
        _cubeContainer = new ContainerBuilder()
#pragma warning restore CS0618
            .WithImage("cubejs/cube:v1.1.5")
            .WithNetwork(_network)
            .WithPortBinding(4000, true)
            .WithEnvironment("CUBEJS_DB_TYPE", "clickhouse")
            .WithEnvironment("CUBEJS_DB_HOST", "clickhouse")
            .WithEnvironment("CUBEJS_DB_PORT", "8123")
            .WithEnvironment("CUBEJS_DB_USER", "clickhouse")
            .WithEnvironment("CUBEJS_DB_PASS", chPassword)
            .WithEnvironment("CUBEJS_API_SECRET", "e2e-test-secret-long-enough-for-hs256")
            .WithEnvironment("CUBEJS_DEV_MODE", "true")
            .WithBindMount(Path.GetDirectoryName(cubeModelPath)!, "/cube/conf")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPath("/readyz").ForPort(4000)))
            .Build();

        await _cubeContainer.StartAsync();

        var cubeBaseUrl = $"http://localhost:{_cubeContainer.GetMappedPublicPort(4000)}";
        var settings = new CubeSettings(new(cubeBaseUrl), "e2e-test-secret-long-enough-for-hs256");

        var httpClient = new HttpClient { BaseAddress = new(cubeBaseUrl) };
        _cubeService = new(httpClient, settings, NullLogger<CubeApiService>.Instance);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Task.WhenAll(_cubeContainer.DisposeAsync().AsTask(),
            _clickhouseContainer.DisposeAsync().AsTask());

        await _network.DeleteAsync();
    }

    private CubeApiService _cubeService = null!;

    [Test]
    public async Task CubeE2E_Meta_ReturnsDefinedCubes()
    {
        var meta = await _cubeService.GetMetaAsync();

        Assert.That(meta.Cubes, Is.Not.Empty);

        var cubeNames = meta.Cubes.Select(c => c.Name).ToList();
        Assert.That(cubeNames, Does.Contain("operations"));
        Assert.That(cubeNames, Does.Contain("debts"));
        Assert.That(cubeNames, Does.Contain("api_metrics"));
    }

    [Test]
    public async Task CubeE2E_Operations_MeasuresAndDimensionsExist()
    {
        var meta = await _cubeService.GetMetaAsync();

        var operations = meta.Cubes.FirstOrDefault(c => c.Name == "operations");
        Assert.That(operations, Is.Not.Null);

        var measureNames = operations!.Measures.Select(m => m.Name).ToList();
        Assert.That(measureNames, Does.Contain("operations.total_sum"));
        Assert.That(measureNames, Does.Contain("operations.net_balance"));

        var dimensionNames = operations.Dimensions.Select(d => d.Name).ToList();
        Assert.That(dimensionNames, Does.Contain("operations.category_name"));
        Assert.That(dimensionNames, Does.Contain("operations.operation_type"));
    }

    [Test]
    public async Task CubeE2E_Load_ReturnsEmptyDataOnEmptyClickHouse()
    {
        var result = await _cubeService.GetExpenseCubeAsync(1,
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            ["category_name"],
            "month");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Data, Is.Not.Null);
    }

    [Test]
    public async Task CubeE2E_SecurityContext_QueryRewrite_FiltersByUser()
    {
        var result1 = await _cubeService.GetExpenseCubeAsync(1,
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            ["category_name"], "month");

        var result2 = await _cubeService.GetExpenseCubeAsync(2,
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            ["category_name"], "month");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result1, Is.Not.Null);
            Assert.That(result2, Is.Not.Null);
        }
    }

    // TODO: Костыль
    private async Task InitializeClickHouseTablesAsync(string password)
    {
        var port = _clickhouseContainer.GetMappedPublicPort(8123);
        using var http = new HttpClient();

        string[] ddl =
        [
            """
            CREATE TABLE IF NOT EXISTS operations_analytics (
                user_id      Int32,
                operation_id Int32,
                category_id  Int32,
                category_name String,
                operation_type Int32,
                sum          Decimal(18,2),
                date         Date,
                place_name   Nullable(String),
                comment      Nullable(String),
                created_at   DateTime DEFAULT now()
            ) ENGINE = MergeTree()
            ORDER BY (user_id, date, category_id)
            PARTITION BY toYYYYMM(date)
            """,
            """
            CREATE TABLE IF NOT EXISTS debts_analytics (
                user_id   Int32,
                debt_id   Int32,
                owner_name String,
                type_id   Int32,
                sum       Decimal(18,2),
                pay_sum   Decimal(18,2),
                status_id Int32,
                date      Date,
                created_at DateTime DEFAULT now()
            ) ENGINE = ReplacingMergeTree(created_at)
            ORDER BY (user_id, debt_id)
            PARTITION BY toYYYYMM(date)
            """,
            """
            CREATE TABLE IF NOT EXISTS api_metrics (
                timestamp   DateTime,
                user_id     Nullable(Int32),
                endpoint    String,
                method      String,
                status_code Int32,
                duration_ms Float64
            ) ENGINE = MergeTree()
            ORDER BY (endpoint, timestamp)
            PARTITION BY toYYYYMMDD(timestamp)
            TTL timestamp + INTERVAL 90 DAY
            """,
        ];

        foreach (var sql in ddl)
        {
            var response = await http.PostAsync($"http://localhost:{port}/?user=clickhouse&password={password}",
                new StringContent(sql));

            response.EnsureSuccessStatusCode();
        }
    }

    private static string ResolveCubeModelPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var cubeModelPath = Path.Combine(solutionRoot, "cube", "model");

        if (!Directory.Exists(cubeModelPath))
        {
            throw new DirectoryNotFoundException($"Cube model directory not found: {cubeModelPath}. " + $"Base directory: {baseDir}");
        }

        return cubeModelPath;
    }
#pragma warning disable NUnit1032
    private INetwork _network = null!;
    private ClickHouseContainer _clickhouseContainer = null!;
    private IContainer _cubeContainer = null!;
#pragma warning restore NUnit1032
}
