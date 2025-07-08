using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Money.Api.Definitions;

/// <summary>
/// Определение для настройки OpenTelemetry наблюдаемости
/// </summary>
public class ObservabilityDefinition : AppDefinition
{
    private const string ServiceName = "Money.Api";
    private const string ServiceVersion = "1.0.0";

    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(ServiceName, ServiceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName,
                ["service.instance.id"] = Environment.MachineName,
            });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracingBuilder =>
            {
                tracingBuilder
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true)
                    .AddSource("Money.Api.*")
                    .AddSource("Money.Business.*")
                    .AddOtlpExporter(options =>
                    {
                        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                                           ?? "http://localhost:4317";

                        options.Endpoint = new(otlpEndpoint);
                        options.Protocol = OtlpExportProtocol.Grpc;
                    });
            })
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("Money.Api.*")
                    .AddPrometheusExporter();
            })
            ;

        builder.Services.AddSingleton(new ActivitySource(ServiceName, ServiceVersion));
        builder.Services.AddSingleton(new Meter(ServiceName, ServiceVersion));

        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions = ActivityTrackingOptions.SpanId
                                              | ActivityTrackingOptions.TraceId
                                              | ActivityTrackingOptions.ParentId;
        });
    }

    public override void ConfigureApplication(WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint();
    }
}
