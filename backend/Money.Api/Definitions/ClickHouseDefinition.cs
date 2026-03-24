using Money.Api.Services.Analytics;

namespace Money.Api.Definitions;

public class ClickHouseDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.Configure<ClickHouseSettings>(builder.Configuration.GetSection("ClickHouse"));

        builder.AddClickHouseDataSource(connectionName: "clickhousedb");

        builder.Services.AddSingleton<ClickHouseService>();
        builder.Services.AddSingleton<ClickHouseInitializer>();
        builder.Services.AddSingleton<ApiMetricsQueue>();
        builder.Services.AddSingleton<AnalyticsInterceptor>();
        builder.Services.AddSingleton<OutboxCursorService>();
    }

    public override void ConfigureApplication(WebApplication app)
    {
        var initializer = app.Services.GetRequiredService<ClickHouseInitializer>();
        _ = initializer.InitializeWithRetryAsync(5, TimeSpan.FromSeconds(2));
    }
}
