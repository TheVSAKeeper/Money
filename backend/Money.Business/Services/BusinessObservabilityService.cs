using System.Diagnostics;

namespace Money.Business.Services;

public class BusinessObservabilityService(ActivitySource activitySource)
{
    private const string ServiceName = "Money.Business";
    private const string ServiceVersion = "1.0.0";

    public void AddTag(string key, object? value)
    {
        Activity.Current?.SetTag(key, value);
    }

    public void AddEvent(string name, ActivityTagsCollection? tags = null)
    {
        var activity = Activity.Current;
        var eventTags = tags ?? [];
        var activityEvent = new ActivityEvent(name, DateTimeOffset.UtcNow, eventTags);

        activity?.AddEvent(activityEvent);
    }

    public void RecordBusinessException(Exception exception, string? businessOperation = null, Dictionary<string, object>? businessContext = null)
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

        if (businessOperation != null)
        {
            activity.SetTag("business.operation", businessOperation);
        }
    }

    public void AddEventToSpan(Activity? activity, string name, ActivityTagsCollection? tags = null)
    {
        if (activity == null)
        {
            return;
        }

        var eventTags = tags ?? [];
        eventTags["event.source"] = ServiceName;
        eventTags["event.timestamp"] = DateTimeOffset.UtcNow.ToString("O");

        activity.AddEvent(new(name, DateTimeOffset.UtcNow, eventTags));
    }

    public Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        var activity = activitySource.StartActivity(operationName, kind);

        if (activity == null)
        {
            return activity;
        }

        EnrichSpanWithBusinessContext(activity);
        return activity;
    }

    public void AddEventWithData(string eventName, Dictionary<string, object>? eventData = null)
    {
        var tags = new ActivityTagsCollection();

        if (eventData != null)
        {
            foreach (var kvp in eventData)
            {
                tags[kvp.Key] = kvp.Value;
            }
        }

        AddEvent(eventName, tags);
    }

    public void RecordException(Exception exception)
    {
        RecordBusinessException(exception);
    }

    public Activity? StartBusinessOperationSpan(string operationType, string entityType, int? entityId = null, int? userId = null)
    {
        var activity = StartActivity($"{entityType}.{operationType}");

        if (activity == null)
        {
            return activity;
        }

        EnrichSpanWithBusinessContext(activity, userId);

        activity.SetTag("operation.type", operationType);
        activity.SetTag("entity.type", entityType);

        if (entityId.HasValue)
        {
            activity.SetTag("entity.id", entityId.Value);
        }

        if (userId.HasValue)
        {
            activity.SetTag("user.id", userId.Value);
        }

        return activity;
    }

    public void AddBusinessOperationStartEvent(string operationType, Dictionary<string, object>? details = null)
    {
        var tags = new ActivityTagsCollection
        {
            ["operation.type"] = operationType,
            ["event.type"] = "business_operation_start",
        };

        if (details != null)
        {
            foreach (var detail in details)
            {
                tags[detail.Key] = detail.Value;
            }
        }

        AddEvent("BusinessOperationStart", tags);
    }

    public void AddBusinessOperationEndEvent(string operationType, bool success, Dictionary<string, object>? details = null)
    {
        var tags = new ActivityTagsCollection
        {
            ["operation.type"] = operationType,
            ["event.type"] = "business_operation_end",
            ["operation.success"] = success,
        };

        if (details != null)
        {
            foreach (var detail in details)
            {
                tags[detail.Key] = detail.Value;
            }
        }

        AddEvent("BusinessOperationEnd", tags);
    }

    public void AddValidationEvent(string entityType, bool isValid, string[]? errors = null)
    {
        var tags = new ActivityTagsCollection
        {
            ["validation.entity_type"] = entityType,
            ["validation.is_valid"] = isValid,
            ["event.type"] = "validation",
        };

        if (errors != null && errors.Length > 0)
        {
            tags["validation.errors"] = string.Join(", ", errors);
        }

        AddEvent("Validation", tags);
    }

    public void AddDatabaseEvent(string operation, string table, int? recordsAffected = null)
    {
        var tags = new ActivityTagsCollection
        {
            ["db.operation"] = operation,
            ["db.table"] = table,
            ["event.type"] = "database",
        };

        if (recordsAffected.HasValue)
        {
            tags["db.records_affected"] = recordsAffected.Value;
        }

        AddEvent("DatabaseOperation", tags);
    }

    public void AddEventToBothCurrentAndSpan(Activity? activity, string name, ActivityTagsCollection? tags = null)
    {
        AddEvent(name, tags);

        if (activity != null && activity != Activity.Current)
        {
            AddEventToSpan(activity, name, tags);
        }
    }

    public Activity? StartNestedBusinessSpan(string operationName, string entityType, ActivityKind kind = ActivityKind.Internal)
    {
        var spanName = $"{entityType}.{operationName}";
        var activity = activitySource.StartActivity(spanName, kind);

        if (activity == null)
        {
            return activity;
        }

        activity.SetTag("operation.type", "business");
        activity.SetTag("operation.entity_type", entityType);
        activity.SetTag("operation.name", operationName);
        activity.SetTag("span.kind", kind.ToString());
        activity.SetTag("service.name", ServiceName);

        EnrichSpanWithBusinessContext(activity);

        return activity;
    }

    public void EnrichAutomaticSpans(string eventName, Dictionary<string, object>? businessContext = null)
    {
        var currentActivity = Activity.Current;

        if (currentActivity == null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            ["event.type"] = "business_enrichment",
            ["event.source"] = ServiceName,
        };

        if (businessContext != null)
        {
            foreach (var kvp in businessContext)
            {
                tags[$"business.{kvp.Key}"] = kvp.Value;
            }
        }

        AddEvent(eventName, tags);
    }

    public void AddBusinessOperationEventToSpan(Activity? activity, string operationType, bool success, Dictionary<string, object>? details = null)
    {
        if (activity == null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            ["business.operation_type"] = operationType,
            ["business.success"] = success,
            ["event.type"] = "business_operation",
            ["event.source"] = ServiceName,
            ["event.timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        if (details != null)
        {
            foreach (var kvp in details)
            {
                tags[$"business.{kvp.Key}"] = kvp.Value;
            }
        }

        var eventName = success ? $"{operationType}_completed" : $"{operationType}_failed";
        AddEventToSpan(activity, eventName, tags);
    }

    public Activity? StartEnhancedBusinessSpan(
        string operationType,
        string entityType,
        int? entityId = null,
        int? userId = null,
        Dictionary<string, object>? additionalTags = null)
    {
        var spanName = $"{entityType}.{operationType}";
        var activity = activitySource.StartActivity(spanName);

        if (activity == null)
        {
            return activity;
        }

        activity.SetTag("operation.type", "business");
        activity.SetTag("operation.entity_type", entityType);
        activity.SetTag("operation.name", operationType);
        activity.SetTag("service.name", ServiceName);

        if (entityId.HasValue)
        {
            activity.SetTag($"{entityType}.id", entityId.Value);
        }

        if (userId.HasValue)
        {
            activity.SetTag("user.id", userId.Value);
        }

        if (additionalTags != null)
        {
            foreach (var kvp in additionalTags)
            {
                activity.SetTag($"business.{kvp.Key}", kvp.Value);
            }
        }

        EnrichSpanWithBusinessContext(activity);

        return activity;
    }

    private void EnrichSpanWithBusinessContext(Activity? activity, int? userId = null)
    {
        if (activity == null)
        {
            return;
        }

        activity.SetTag("service.name", ServiceName);
        activity.SetTag("service.version", ServiceVersion);
        activity.SetTag("operation.timestamp", DateTimeOffset.UtcNow.ToString("O"));

        if (userId.HasValue)
        {
            activity.SetTag("user.id", userId.Value);
        }
    }
}
