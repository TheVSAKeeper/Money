using Microsoft.Extensions.Options;
using Money.Api.Services.Lakehouse;

namespace Money.Api.BackgroundServices;

public sealed class LakehouseTransformBackgroundService(
    LakehouseTransformService transformService,
    IOptions<LakehouseSettings> settings,
    ILogger<LakehouseTransformBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(settings.Value.TransformIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await transformService.TransformBronzeToSilverAsync(stoppingToken);
                await transformService.TransformSilverToGoldAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Lakehouse transform failed — will retry next interval");
            }
        }
    }
}
