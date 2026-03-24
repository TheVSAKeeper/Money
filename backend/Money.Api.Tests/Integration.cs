using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Money.Data;
using Money.Data.Sharding;
using Neo4j.Driver;
using System.Collections.Concurrent;
using Testcontainers.ClickHouse;
using Testcontainers.Neo4j;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Money.Api.Tests;

[SetUpFixture]
public class Integration
{
    private static readonly ConcurrentBag<HttpClient> HttpClients = [];

    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    public static TestServer TestServer { get; private set; } = null!;

    public static DatabaseClient GetDatabaseClient()
    {
        var config = TestServer.Services.GetRequiredService<IConfigurationRoot>();
        var routingConnectionString = config.GetConnectionString("RoutingDb");

        var shardFactory = ServiceProvider.GetRequiredService<ShardedDbContextFactory>();
        var shardRouter = ServiceProvider.GetRequiredService<ShardRouter>();

        return new(shardFactory, shardRouter, CreateRoutingDbContext, new(GetHttpClient(), Console.WriteLine));

        RoutingDbContext CreateRoutingDbContext()
        {
            DbContextOptionsBuilder<RoutingDbContext> optionsBuilder = new();
            optionsBuilder.UseNpgsql(routingConnectionString);
            optionsBuilder.UseSnakeCaseNamingConvention();
            optionsBuilder.EnableSensitiveDataLogging();
            return new(optionsBuilder.Options);
        }
    }

    public static HttpClient GetHttpClient()
    {
        var client = TestServer.CreateClient();
        HttpClients.Add(client);
        return client;
    }

    public static IDriver GetNeo4jDriver()
    {
        return ServiceProvider.GetRequiredService<IDriver>();
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _routingContainer = new PostgreSqlBuilder("postgres:17.6")
            .WithDatabase("money_routing")
            .Build();

        _dundukContainer = new PostgreSqlBuilder("postgres:17.6")
            .WithDatabase("money_dunduk")
            .Build();

        _fundukContainer = new PostgreSqlBuilder("postgres:17.6")
            .WithDatabase("money_funduk")
            .Build();

        _burundukContainer = new PostgreSqlBuilder("postgres:17.6")
            .WithDatabase("money_burunduk")
            .Build();

        _redisContainer = new RedisBuilder("redis:8.2").Build();
        _clickHouseContainer = new ClickHouseBuilder("clickhouse/clickhouse-server:26.2-alpine").Build();
        _neo4jContainer = new Neo4jBuilder("neo4j:2026.02.3").Build();

        await Task.WhenAll(_routingContainer.StartAsync(),
            _dundukContainer.StartAsync(),
            _fundukContainer.StartAsync(),
            _burundukContainer.StartAsync(),
            _redisContainer.StartAsync(),
            _clickHouseContainer.StartAsync(),
            _neo4jContainer.StartAsync());

        CustomWebApplicationFactory<Program> webHostBuilder = new()
        {
            RoutingDb = _routingContainer.GetConnectionString(),
            DundukDb = _dundukContainer.GetConnectionString(),
            FundukDb = _fundukContainer.GetConnectionString(),
            BurundukDb = _burundukContainer.GetConnectionString(),
            RedisConnectionString = _redisContainer.GetConnectionString(),
            ClickHouseConnectionString = _clickHouseContainer.GetConnectionString(),
            Neo4jBoltUri = _neo4jContainer.GetConnectionString(),
        };

        webHostBuilder.Server.PreserveExecutionContext = true;

        ServiceProvider = webHostBuilder.Services;
        TestServer = webHostBuilder.Server;
    }

    [OneTimeTearDown]
    public Task OneTimeTearDown()
    {
        TestServer.Dispose();

        foreach (var httpClient in HttpClients.ToArray())
        {
            httpClient.Dispose();
        }

        return Task.WhenAll(_routingContainer.DisposeAsync().AsTask(),
            _dundukContainer.DisposeAsync().AsTask(),
            _fundukContainer.DisposeAsync().AsTask(),
            _burundukContainer.DisposeAsync().AsTask(),
            _redisContainer.DisposeAsync().AsTask(),
            _clickHouseContainer.DisposeAsync().AsTask(),
            _neo4jContainer.DisposeAsync().AsTask());
    }

#pragma warning disable NUnit1032
    private static PostgreSqlContainer _routingContainer = null!;
    private static PostgreSqlContainer _dundukContainer = null!;
    private static PostgreSqlContainer _fundukContainer = null!;
    private static PostgreSqlContainer _burundukContainer = null!;
    private static RedisContainer _redisContainer = null!;
    private static ClickHouseContainer _clickHouseContainer = null!;
    private static Neo4jContainer _neo4jContainer = null!;
#pragma warning restore NUnit1032
}
