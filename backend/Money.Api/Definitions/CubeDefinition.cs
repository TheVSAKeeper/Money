using Money.Api.Services.Analytics;

namespace Money.Api.Definitions;

public class CubeDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        var cubeUrl = builder.Configuration["Cube:BaseUrl"] ?? "http://localhost:4000";

        var apiSecret = builder.Configuration["Cube:ApiSecret"];

        if (string.IsNullOrWhiteSpace(apiSecret))
        {
            return;
        }

        builder.Services.AddSingleton(new CubeSettings(new(cubeUrl), apiSecret));

        builder.Services.AddHttpClient<CubeApiService>(client =>
        {
            client.BaseAddress = new(cubeUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
        });
    }
}
