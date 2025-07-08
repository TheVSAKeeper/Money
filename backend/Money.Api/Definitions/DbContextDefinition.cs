using Microsoft.EntityFrameworkCore;
using Money.Api.Interceptors;
using Money.Data;
using System.Globalization;

namespace Money.Api.Definitions;

public class DbContextDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddDbContextPool<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(builder.Configuration.GetConnectionString(nameof(ApplicationDbContext)));
            options.UseSnakeCaseNamingConvention();
            options.UseOpenIddict();

            var interceptor = serviceProvider.GetRequiredService<EfCoreBusinessContextInterceptor>();
            options.AddInterceptors(interceptor);
        });
    }

    public override void ConfigureApplication(WebApplication app)
    {
        var automigrate = app.Configuration["AUTO_MIGRATE"];

        if (automigrate?.ToLower(CultureInfo.InvariantCulture) == "true" || automigrate == "1")
        {
            app.Services.InitializeDatabaseContext();
        }
    }
}
