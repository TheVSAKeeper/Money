using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Money.Api.Services;
using Money.Business;
using NLog;
using System.Diagnostics;

namespace Money.Api.Middlewares;

public class ObservabilityMiddleware(RequestDelegate next, ObservabilityService observabilityService)
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public async Task InvokeAsync(HttpContext context, RequestEnvironment requestEnvironment)
    {
        var stopwatch = Stopwatch.StartNew();
        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        using var customActivity = observabilityService.StartActivity($"{method} {path}", ActivityKind.Server);
        var activity = Activity.Current;

        observabilityService.AddTag("http.method", method);
        observabilityService.AddTag("http.url", context.Request.GetDisplayUrl());
        observabilityService.AddTag("http.scheme", context.Request.Scheme);
        observabilityService.AddTag("http.host", context.Request.Host.Value);
        observabilityService.AddTag("http.target", path);
        observabilityService.AddTag("user_agent", context.Request.Headers.UserAgent.ToString());

        if (context.User.Identity?.IsAuthenticated == true)
        {
            observabilityService.AddTag("user.id", context.User.Identity.Name);
        }

        _logger.Info("HTTP Request started: {Method} {Path}", method, path);

        try
        {
            if (activity != null)
            {
                EnrichWithUserContext(activity, context, requestEnvironment);
                EnrichWithRequestContext(activity, context);
                EnrichWithRouteContext(activity, context);
            }

            await next(context);

            if (activity != null)
            {
                EnrichWithResponseContext(activity, context);
            }
        }
        catch (Exception exception)
        {
            if (activity != null)
            {
                EnrichWithErrorContext(activity, exception, observabilityService);
            }

            observabilityService.RecordException(exception);
            _logger.Error(exception, "HTTP Request failed: {Method} {Path}", method, path);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var duration = stopwatch.Elapsed.TotalSeconds;
            var statusCode = context.Response.StatusCode;

            observabilityService.AddTag("http.status_code", statusCode);
            observabilityService.AddTag("http.response_size", context.Response.ContentLength);

            observabilityService.RecordHttpRequest(method, path, statusCode, duration);

            _logger.Info("HTTP Request completed: {Method} {Path} {StatusCode} in {Duration}ms",
                method, path, statusCode, stopwatch.ElapsedMilliseconds);
        }
    }

    private static void EnrichWithUserContext(Activity activity, HttpContext context, RequestEnvironment requestEnvironment)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            activity.SetTag("user.authenticated", true);
            activity.SetTag("user.name", context.User.Identity.Name);

            if (requestEnvironment.AuthUser?.Id != null)
            {
                activity.SetTag("user.domain_id", requestEnvironment.AuthUser?.Id);
            }

            var subClaim = context.User.FindFirst("sub")?.Value;

            if (!string.IsNullOrEmpty(subClaim))
            {
                activity.SetTag("user.auth_id", subClaim);
            }

            var roleClaims = context.User.FindAll("role").Select(c => c.Value).ToArray();

            if (roleClaims.Length > 0)
            {
                activity.SetTag("user.roles", string.Join(",", roleClaims));
            }
        }
        else
        {
            activity.SetTag("user.authenticated", false);
        }
    }

    private static void EnrichWithRequestContext(Activity activity, HttpContext context)
    {
        if (context.Request.ContentLength.HasValue)
        {
            activity.SetTag("http.request.body.size", context.Request.ContentLength.Value);
        }

        if (!string.IsNullOrEmpty(context.Request.ContentType))
        {
            activity.SetTag("http.request.content_type", context.Request.ContentType);
        }

        if (context.Request.Query.Count > 0)
        {
            activity.SetTag("http.request.query.count", context.Request.Query.Count);

            if (context.Request.Query.TryGetValue("categoryId", out var categoryId))
            {
                activity.SetTag("business.category_id", categoryId.ToString());
            }

            if (context.Request.Query.TryGetValue("operationId", out var operationId))
            {
                activity.SetTag("business.operation_id", operationId.ToString());
            }
        }
    }

    private static void EnrichWithRouteContext(Activity activity, HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        var actionDescriptor = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();

        if (actionDescriptor == null)
        {
            return;
        }

        activity.SetTag("controller.name", actionDescriptor.ControllerName);
        activity.SetTag("action.name", actionDescriptor.ActionName);
        activity.SetTag("operation.name", $"{actionDescriptor.ControllerName}.{actionDescriptor.ActionName}");

        if (context.Request.RouteValues.Count > 0)
        {
            foreach (var routeValue in context.Request.RouteValues.Where(routeValue => routeValue.Key != "controller" && routeValue.Key != "action"))
            {
                activity.SetTag($"route.{routeValue.Key}", routeValue.Value?.ToString());
            }
        }

        var businessOperationType = actionDescriptor.ControllerName.ToLowerInvariant() switch
        {
            "operations" => "financial_operation",
            "accounts" => "account_management",
            "categories" => "category_management",
            "debts" => "debt_management",
            "auth" => "authentication",
            _ => "general",
        };

        activity.SetTag("business.operation_type", businessOperationType);
    }

    private static void EnrichWithResponseContext(Activity activity, HttpContext context)
    {
        activity.SetTag("http.response.status_code", context.Response.StatusCode);

        if (context.Response.ContentLength.HasValue)
        {
            activity.SetTag("http.response.body.size", context.Response.ContentLength.Value);
        }

        if (!string.IsNullOrEmpty(context.Response.ContentType))
        {
            activity.SetTag("http.response.content_type", context.Response.ContentType);
        }

        var isSuccess = context.Response.StatusCode >= 200 && context.Response.StatusCode < 300;
        activity.SetTag("operation.success", isSuccess);

        if (isSuccess)
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            activity.SetStatus(ActivityStatusCode.Error, $"HTTP {context.Response.StatusCode}");
        }
    }

    private static void EnrichWithErrorContext(Activity activity, Exception exception, ObservabilityService observabilityService)
    {
        activity.SetTag("error.occurred", true);
        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("error.message", exception.Message);

        var businessErrorType = exception.GetType().Name switch
        {
            "EntityExistsException" => "business_validation_error",
            "NotFoundException" => "entity_not_found",
            "PermissionException" => "authorization_error",
            "BusinessException" => "business_logic_error",
            "IncorrectDataException" => "data_validation_error",
            _ => "system_error",
        };

        activity.SetTag("business.error_type", businessErrorType);

        observabilityService.AddEvent("BusinessError", new()
        {
            ["error.type"] = exception.GetType().Name,
            ["business.error_type"] = businessErrorType,
            ["error.message"] = exception.Message,
        });
    }
}
