#pragma warning disable S2139
using NLog;
using NLog.Web;

var logger = LogManager.Setup()
    .LoadConfigurationFromAppSettings()
    .GetCurrentClassLogger();

logger.Debug("init main");

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    builder.AddDefinitions(typeof(Program));

    var app = builder.Build();

    app.UseDefinitions();
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    await app.RunAsync();
}
catch (Exception exception)
{
    logger.Error(exception, "Stopped program because of exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}

public partial class Program
{
    protected Program()
    {
    }
}
