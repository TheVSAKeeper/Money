using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
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
    public string? Neo4jBoltUri { get; set; }
    public string? MinioConnectionString { get; set; }

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

        if (Neo4jBoltUri != null)
        {
            builderConfig.AddInMemoryCollection([
                new("Neo4j:BoltUri", Neo4jBoltUri),
                new("Neo4j:User", "neo4j"),
                new("Neo4j:Password", ""),
            ]);
        }

        if (MinioConnectionString != null)
        {
            var minioUri = new Uri(MinioConnectionString);
            var minioEndpoint = $"{minioUri.Host}:{minioUri.Port}";

            builderConfig.AddInMemoryCollection([
                new("Lakehouse:MinioEndpoint", minioEndpoint),
                new("Lakehouse:MinioAccessKey", "AKIAIOSFODNN7EXAMPLE"),
                new("Lakehouse:MinioSecretKey", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"),
                new("Lakehouse:Warehouse", "s3://lakehouse-warehouse/"),
                new("Lakehouse:SyncIntervalSeconds", "0.1"),
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

            if (MinioConnectionString != null)
            {
                var minioUri = new Uri(MinioConnectionString);
                var minioEndpoint = $"{minioUri.Host}:{minioUri.Port}";

                var minioDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMinioClient));

                if (minioDescriptor != null)
                {
                    services.Remove(minioDescriptor);
                }

                var minioClient = (IMinioClient)new MinioClient()
                    .WithEndpoint(minioEndpoint)
                    .WithCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")
                    .WithSSL(false)
                    .Build();

                var bucketExists = minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket("lakehouse-warehouse")).GetAwaiter().GetResult();

                if (!bucketExists)
                {
                    minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket("lakehouse-warehouse")).GetAwaiter().GetResult();
                }

                services.AddSingleton(minioClient);
            }

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
