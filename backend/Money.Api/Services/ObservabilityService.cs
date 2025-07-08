using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Money.Api.Services;

public class ObservabilityService(ActivitySource activitySource, Meter meter)
{
    private readonly Counter<long> _requestCounter = meter.CreateCounter<long>("money_api_requests_total",
        "requests",
        "Total number of HTTP requests");

    private readonly Counter<long> _errorCounter = meter.CreateCounter<long>("money_api_errors_total",
        "errors",
        "Total number of errors");

    private readonly Histogram<double> _requestDuration = meter.CreateHistogram<double>("money_api_request_duration_seconds",
        "seconds",
        "Duration of HTTP requests");

    public Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        return activitySource.StartActivity(operationName, kind);
    }

    public void RecordHttpRequest(string method, string path, int statusCode, double duration)
    {
        var tags = new TagList
        {
            new("method", method),
            new("path", path),
            new("status_code", statusCode.ToString()),
        };

        _requestCounter.Add(1, tags);
        _requestDuration.Record(duration, tags);

        if (statusCode >= 400)
        {
            _errorCounter.Add(1, tags);
        }
    }

    public void AddTag(string key, object? value)
    {
        Activity.Current?.SetTag(key, value);
    }

    public void AddEvent(string name, ActivityTagsCollection? tags = null)
    {
        Activity.Current?.AddEvent(new(name, DateTimeOffset.UtcNow, tags ?? new ActivityTagsCollection()));
    }

    public void RecordException(Exception exception)
    {
        var activity = Activity.Current;

        if (activity == null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
        activity.SetTag("exception.stacktrace", exception.StackTrace);
    }
}
