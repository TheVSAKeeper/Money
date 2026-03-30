using Minio;
using Money.Api.BackgroundServices;
using Money.Api.Services.Lakehouse;

namespace Money.Api.Definitions;

public class LakehouseDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Lakehouse");
        builder.Services.Configure<LakehouseSettings>(config);

        var settings = config.Get<LakehouseSettings>() ?? new LakehouseSettings();

        builder.Services.AddSingleton<IMinioClient>(_ =>
            new MinioClient()
                .WithEndpoint(settings.MinioEndpoint)
                .WithCredentials(settings.MinioAccessKey, settings.MinioSecretKey)
                .WithSSL(false)
                .Build());

        builder.Services.AddScoped<LakehouseWriter>();
        builder.Services.AddSingleton<LakehouseTransformService>();
        builder.Services.AddSingleton<LakehouseQueryService>();

        builder.Services.AddHttpClient("trino", client =>
        {
            client.BaseAddress = new Uri(settings.TrinoUri);
        });

        builder.Services.AddSingleton<LakehouseSyncService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LakehouseSyncService>());

        builder.Services.AddSingleton<LakehouseReconciliationService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LakehouseReconciliationService>());

        builder.Services.AddHostedService<LakehouseTransformBackgroundService>();
    }
}
