using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddDefinitions(typeof(Program));

var app = builder.Build();

app.UseDefinitions();

app.MapDefaultEndpoints();

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var culture = new CultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await app.RunAsync();

public partial class Program
{
    protected Program()
    {
    }
}
