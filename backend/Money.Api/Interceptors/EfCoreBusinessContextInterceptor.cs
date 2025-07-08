using Microsoft.EntityFrameworkCore.Diagnostics;
using Money.Api.Services;
using System.Data.Common;
using System.Diagnostics;

namespace Money.Api.Interceptors;

public class EfCoreBusinessContextInterceptor(
    IServiceScopeFactory serviceProvider,
    ILogger<EfCoreBusinessContextInterceptor> logger)
    : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        EnrichSpanWithBusinessContext(command, "SELECT");
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        EnrichSpanWithBusinessContext(command, "SELECT");
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        EnrichSpanWithBusinessContext(command, "MODIFY");
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnrichSpanWithBusinessContext(command, "MODIFY");
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        EnrichSpanWithBusinessContext(command, "SCALAR");
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        EnrichSpanWithBusinessContext(command, "SCALAR");
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogDatabaseOperationCompleted(eventData, "SELECT", true);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogDatabaseOperationCompleted(eventData, "SELECT", true);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogDatabaseOperationCompleted(eventData, "MODIFY", true, result);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogDatabaseOperationCompleted(eventData, "MODIFY", true, result);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        LogDatabaseOperationCompleted(eventData, "FAILED", false);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogDatabaseOperationCompleted(eventData, "FAILED", false);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    private static string GetBusinessCategory(string entityType)
    {
        return entityType switch
        {
            "financial_operation" or "fast_operation" or "regular_operation" => "financial",
            "debt" or "debt_owner" => "debt_management",
            "category" or "place" => "reference_data",
            "vehicle" or "vehicle_event" => "vehicle_management",
            "user" or "identity_user" => "user_management",
            "oauth_application" or "oauth_authorization" or "oauth_scope" or "oauth_token" => "authentication",
            _ => "general",
        };
    }

    private void EnrichSpanWithBusinessContext(DbCommand command, string operationType)
    {
        var activity = Activity.Current;

        if (activity == null)
        {
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var observabilityService = scope.ServiceProvider.GetService<BusinessObservabilityService>();

            if (observabilityService == null)
            {
                return;
            }

            var businessContext = BusinessContextExtractor.ExtractBusinessContext(command, activity);

            observabilityService.AddTag("db.operation_type", operationType);

            foreach (var kvp in businessContext)
            {
                observabilityService.AddTag(kvp.Key, kvp.Value);
            }

            if (businessContext.TryGetValue("business.entity_type", out var value))
            {
                var entityType = value.ToString();

                if (entityType != null)
                {
                    observabilityService.AddTag("business.primary_entity", entityType);
                    observabilityService.AddTag("business.category", GetBusinessCategory(entityType));
                }
            }

            var priority = BusinessContextExtractor.GetBusinessOperationPriority(businessContext);
            observabilityService.AddTag("business.priority", priority);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enrich EF Core span with business context");
        }
    }

    private void LogDatabaseOperationCompleted(
        CommandEventData eventData,
        string operationType,
        bool success,
        int? recordsAffected = null)
    {
        var activity = Activity.Current;

        if (activity == null)
        {
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var observabilityService = scope.ServiceProvider.GetService<BusinessObservabilityService>();

            if (observabilityService == null)
            {
                return;
            }

            var duration = eventData is CommandExecutedEventData executedEventData
                ? executedEventData.Duration.TotalMilliseconds
                : 0;

            observabilityService.AddTag("db.operation_success", success);
            observabilityService.AddTag("db.operation_duration_ms", duration);

            if (recordsAffected.HasValue)
            {
                observabilityService.AddTag("db.records_affected", recordsAffected.Value);
            }

            var eventDetails = new Dictionary<string, object>
            {
                ["operation_type"] = operationType,
                ["success"] = success,
                ["duration_ms"] = duration,
            };

            if (recordsAffected.HasValue)
            {
                eventDetails["records_affected"] = recordsAffected.Value;
            }

            if (!success && eventData is CommandErrorEventData errorEventData)
            {
                eventDetails["error_message"] = errorEventData.Exception.Message;
                eventDetails["error_type"] = errorEventData.Exception.GetType().Name;

                observabilityService.AddTag("db.error_type", errorEventData.Exception.GetType().Name);
            }

            observabilityService.AddEventWithData("DatabaseOperationCompleted", eventDetails);

            logger.Log(success ? LogLevel.Debug : LogLevel.Warning,
                "Database operation completed: {OperationType} in {Duration} ms, Success: {Success}, Records: {Records}",
                operationType, duration, success, recordsAffected);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log database operation completion");
        }
    }
}
