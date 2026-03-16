using Money.Api.Services.Cache;
using Money.Api.Services.Locks;
using Money.Api.Services.Notifications;
using Money.Api.Services.Queue;
using Money.Business;
using Money.Business.Interfaces;

namespace Money.Api.Definitions;

public class ContainerDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<RequestEnvironment>();

        builder.Services.AddScoped<AccountsService>();
        builder.Services.AddScoped<AuthService>();

        builder.Services.AddScoped<CategoriesService>();
        builder.Services.AddScoped<DebtsService>();
        builder.Services.AddScoped<OperationsService>();
        builder.Services.AddScoped<UsersService>();
        builder.Services.AddScoped<FastOperationsService>();
        builder.Services.AddScoped<PlacesService>();
        builder.Services.AddScoped<RegularOperationsService>();
        builder.Services.AddScoped<FilesService>();
        builder.Services.AddScoped<CarsService>();
        builder.Services.AddScoped<CarEventsService>();
        builder.Services.AddSingleton<IEmailQueueService, RedisEmailQueueService>();

        builder.Services.AddSingleton<ICategoryCacheService, CategoryCacheService>();
        builder.Services.AddSingleton<IOperationCacheService, OperationCacheService>();
        builder.Services.AddSingleton<ICounterCacheService, CounterCacheService>();
        builder.Services.AddSingleton<IDistributedLockService, RedisLockService>();
        builder.Services.AddSingleton<INotificationService, RedisNotificationService>();
        builder.Services.AddSingleton<AdminNotificationPublisher>();
    }
}
