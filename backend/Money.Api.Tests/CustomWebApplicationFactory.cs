using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Money.Api.Services.Analytics;
using Money.Business.Services;
using StackExchange.Redis;

namespace Money.Api.Tests;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    public string? RoutingDb { get; set; }
    public string? DundukDb { get; set; }
    public string? FundukDb { get; set; }
    public string? BurundukDb { get; set; }
    public string? RedisConnectionString { get; set; }
    public string? ClickHouseConnectionString { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var env = "Development";

        var builderConfig = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile($"appsettings.{env}.json");

        if (RoutingDb != null)
        {
            builderConfig.AddInMemoryCollection([new("ConnectionStrings:RoutingDb", RoutingDb)]);
        }

        if (DundukDb != null)
        {
            builderConfig.AddInMemoryCollection([new("ConnectionStrings:DundukDb", DundukDb)]);
        }

        if (FundukDb != null)
        {
            builderConfig.AddInMemoryCollection([new("ConnectionStrings:FundukDb", FundukDb)]);
        }

        if (BurundukDb != null)
        {
            builderConfig.AddInMemoryCollection([new("ConnectionStrings:BurundukDb", BurundukDb)]);
        }

        if (ClickHouseConnectionString != null)
        {
            builderConfig.AddInMemoryCollection([
                new("ConnectionStrings:clickhousedb", ClickHouseConnectionString),
                new("ClickHouse:SyncIntervalSeconds", "0.1"),
            ]);
        }

        var configRoot = builderConfig.Build();

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(configRoot);
            services.AddSingleton(configRoot);
            services.AddSingleton<IMailsService, TestMailsService>();

            const string testCubeSecret = "test-cube-secret-for-integration-tests";
            var mockCubeHandler = new MockCubeHttpHandler();
            services.AddSingleton(mockCubeHandler);
            services.AddSingleton(new CubeSettings(new("http://cube-mock:4000"), testCubeSecret));
            services.AddHttpClient<CubeApiService>(client =>
                {
                    client.BaseAddress = new("http://cube-mock:4000");
                    client.Timeout = TimeSpan.FromSeconds(10);
                })
                .ConfigurePrimaryHttpMessageHandler(_ => mockCubeHandler);

            if (RedisConnectionString == null)
            {
                return;
            }

            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(RedisConnectionString));
        });

        builder.ConfigureLogging(logging => logging.AddProvider(new NUnitLoggerProvider()));

        builder.UseConfiguration(configRoot);
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        builder.UseEnvironment(env);
    }
}
