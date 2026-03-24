using Money.Api.BackgroundServices;
using Money.Api.HealthChecks;
using Money.Data.Graph;
using Neo4j.Driver;

namespace Money.Api.Definitions;

public class Neo4jDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        var boltUri = builder.Configuration["Neo4j:BoltUri"] ?? "bolt://localhost:7687";
        var user = builder.Configuration["Neo4j:User"] ?? "neo4j";
        var password = builder.Configuration["Neo4j:Password"] ?? "money_password";

        var authToken = string.IsNullOrEmpty(password)
            ? AuthTokens.None
            : AuthTokens.Basic(user, password);

        builder.Services.AddSingleton<IDriver>(_ =>
            GraphDatabase.Driver(boltUri, authToken, o =>
            {
                o.WithEncryptionLevel(EncryptionLevel.None);
                o.WithMaxTransactionRetryTime(TimeSpan.FromSeconds(5));
                o.WithConnectionAcquisitionTimeout(TimeSpan.FromSeconds(10));
            }));

        builder.Services.AddScoped<Neo4jSessionFactory>();
        builder.Services.AddScoped<IDebtGraphService, DebtGraphService>();
        builder.Services.AddScoped<ICategoryGraphService, CategoryGraphService>();
        builder.Services.AddSingleton<Neo4jSchemaInitializer>();

        builder.Services.AddSingleton<Neo4jSyncService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Neo4jSyncService>());

        builder.Services.AddSingleton<GraphReconciliationService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GraphReconciliationService>());

        builder.Services.AddHealthChecks()
            .AddCheck<Neo4jHealthCheck>("neo4j");
    }
}
