using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Money.Business.Services;

namespace Money.Api.Tests;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    public string? RoutingDb { get; set; }
    public string? DundukDb { get; set; }
    public string? FundukDb { get; set; }
    public string? BurundukDb { get; set; }

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

        var configRoot = builderConfig.Build();

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(configRoot);
            services.AddSingleton(configRoot);
            services.AddSingleton<IMailsService, TestMailsService>();
        });

        builder.UseConfiguration(configRoot);
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        builder.UseEnvironment(env);
    }
}
