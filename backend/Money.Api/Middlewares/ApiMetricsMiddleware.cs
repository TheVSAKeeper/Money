using Money.Api.Services.Analytics;
using Money.Business;
using System.Diagnostics;

namespace Money.Api.Middlewares;

public class ApiMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApiMetricsQueue metricsQueue, RequestEnvironment requestEnvironment)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();

            try
            {
                var endpoint = context.GetEndpoint();
                var routePattern = (endpoint as RouteEndpoint)?.RoutePattern?.RawText
                                   ?? context.Request.Path.Value
                                   ?? "unknown";

                metricsQueue.Metrics.Enqueue(new()
                {
                    Timestamp = DateTime.UtcNow,
                    UserId = requestEnvironment.TryGetUserId(),
                    Endpoint = routePattern,
                    Method = context.Request.Method,
                    StatusCode = context.Response.StatusCode,
                    DurationMs = sw.Elapsed.TotalMilliseconds,
                });
            }
            catch
            {
            }
        }
    }
}
