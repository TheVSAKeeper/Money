using Money.Api.BackgroundServices;
using Money.Api.Services.Notifications;

namespace Money.Api.Definitions;

public class BackgroundServicesDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddHostedService<RegularTaskBackgroundService>();
        builder.Services.AddHostedService<EmailSenderBackgroundService>();

        builder.Services.AddSingleton<PartitionMaintenanceService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PartitionMaintenanceService>());

        builder.Services.AddHostedService<CounterSyncService>();
        builder.Services.AddHostedService<NotificationBridgeService>();
        builder.Services.AddHostedService<AdminBridgeService>();

        builder.Services.AddSingleton<ClickHouseSyncService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ClickHouseSyncService>());

        builder.Services.AddSingleton<ClickHouseMetricsService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ClickHouseMetricsService>());
    }
}
