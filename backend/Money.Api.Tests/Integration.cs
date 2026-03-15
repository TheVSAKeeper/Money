using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Money.Data;
using Money.Data.Sharding;
using System.Collections.Concurrent;
using Testcontainers.PostgreSql;

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

        await Task.WhenAll(_routingContainer.StartAsync(),
            _dundukContainer.StartAsync(),
            _fundukContainer.StartAsync(),
            _burundukContainer.StartAsync());

        CustomWebApplicationFactory<Program> webHostBuilder = new()
        {
            RoutingDb = _routingContainer.GetConnectionString(),
            DundukDb = _dundukContainer.GetConnectionString(),
            FundukDb = _fundukContainer.GetConnectionString(),
            BurundukDb = _burundukContainer.GetConnectionString(),
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
            _burundukContainer.DisposeAsync().AsTask());
    }

#pragma warning disable NUnit1032
    private static PostgreSqlContainer _routingContainer = null!;
    private static PostgreSqlContainer _dundukContainer = null!;
    private static PostgreSqlContainer _fundukContainer = null!;
    private static PostgreSqlContainer _burundukContainer = null!;
#pragma warning restore NUnit1032
}
