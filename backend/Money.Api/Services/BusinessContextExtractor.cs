using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Money.Api.Services;

public static partial class BusinessContextExtractor
{
    private static readonly Dictionary<string, string> TableToEntityMapping = new()
    {
        { "categories", "category" },
        { "operations", "financial_operation" },
        { "fast_operations", "fast_operation" },
        { "regular_operations", "regular_operation" },
        { "places", "place" },
        { "debts", "debt" },
        { "debt_owners", "debt_owner" },
        { "cars", "vehicle" },
        { "car_events", "vehicle_event" },
        { "domain_users", "user" },

        { "asp_net_users", "identity_user" },
        { "asp_net_roles", "identity_role" },
        { "asp_net_user_roles", "identity_user_role" },
        { "asp_net_user_claims", "identity_user_claim" },
        { "asp_net_role_claims", "identity_role_claim" },

        { "openiddict_applications", "oauth_application" },
        { "openiddict_authorizations", "oauth_authorization" },
        { "openiddict_scopes", "oauth_scope" },
        { "openiddict_tokens", "oauth_token" },
    };

    private static readonly Dictionary<string, string> SqlOperationToBusinessOperation = new()
    {
        { "SELECT", "read" },
        { "INSERT", "create" },
        { "UPDATE", "update" },
        { "DELETE", "delete" },
        { "MERGE", "upsert" },
    };

    public static string GetBusinessOperationPriority(Dictionary<string, object> context)
    {
        if (context.TryGetValue("business.entity_type", out var value) == false)
        {
            return "low";
        }

        var entityType = value.ToString();

        return entityType switch
        {
            "financial_operation" or "fast_operation" or "regular_operation" => "high",
            "debt" or "category" => "medium",
            _ => "low",
        };
    }

    public static Dictionary<string, object> ExtractBusinessContext(DbCommand command, Activity? activity)
    {
        var context = new Dictionary<string, object>();

        ExtractSqlContext(command, context);
        ExtractSpanContext(activity, context);

        return context;
    }

    [GeneratedRegex(@"\b(?:FROM|JOIN|INTO|UPDATE)\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex TableNameRegex();

    [GeneratedRegex(@"^\s*(SELECT|INSERT|UPDATE|DELETE|MERGE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex SqlOperationRegex();

    private static void ExtractSqlContext(DbCommand command, Dictionary<string, object> context)
    {
        var sql = command.CommandText;

        if (string.IsNullOrEmpty(sql))
        {
            return;
        }

        var operationMatch = SqlOperationRegex().Match(sql);

        if (operationMatch.Success)
        {
            var sqlOperation = operationMatch.Groups[1].Value.ToUpperInvariant();
            context["db.sql_operation"] = sqlOperation;

            if (SqlOperationToBusinessOperation.TryGetValue(sqlOperation, out var businessOperation))
            {
                context["business.operation_type"] = businessOperation;
            }
        }

        var tableMatches = TableNameRegex().Matches(sql);
        var tables = new HashSet<string>();
        var entityTypes = new HashSet<string>();

        foreach (Match match in tableMatches)
        {
            var tableName = match.Groups[1].Value.ToLowerInvariant();
            tables.Add(tableName);

            if (TableToEntityMapping.TryGetValue(tableName, out var entityType))
            {
                entityTypes.Add(entityType);
            }
        }

        if (tables.Count > 0)
        {
            context["db.tables"] = string.Join(",", tables);
        }

        if (entityTypes.Count > 0)
        {
            context["business.entity_types"] = string.Join(",", entityTypes);

            if (entityTypes.Count == 1)
            {
                context["business.entity_type"] = entityTypes.First();
            }
        }

        if (command.Parameters.Count > 0)
        {
            context["db.parameter_count"] = command.Parameters.Count;

            ExtractBusinessParameters(command, context);
        }
    }

    private static void ExtractBusinessParameters(DbCommand command, Dictionary<string, object> context)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            var paramName = parameter.ParameterName?.ToLowerInvariant();

            if (string.IsNullOrEmpty(paramName))
            {
                continue;
            }

            switch (paramName)
            {
                case var name when name.Contains("userid") || name.Contains("user_id"):
                    if (parameter.Value != null && parameter.Value != DBNull.Value)
                    {
                        context["business.user_id"] = parameter.Value.ToString();
                    }

                    break;

                case var name when name.Contains("categoryid") || name.Contains("category_id"):
                    if (parameter.Value != null && parameter.Value != DBNull.Value)
                    {
                        context["business.category_id"] = parameter.Value.ToString();
                    }

                    break;

                case var name when name.Contains("operationid") || name.Contains("operation_id"):
                    if (parameter.Value != null && parameter.Value != DBNull.Value)
                    {
                        context["business.operation_id"] = parameter.Value.ToString();
                    }

                    break;

                case var name when name.Contains("sum") || name.Contains("amount"):
                    if (parameter.Value != null && parameter.Value != DBNull.Value)
                    {
                        context["business.amount"] = parameter.Value.ToString();
                    }

                    break;
            }
        }
    }

    private static void ExtractSpanContext(Activity? activity, Dictionary<string, object> context)
    {
        if (activity == null)
        {
            return;
        }

        foreach (var tag in activity.Tags)
        {
            if (tag.Key.StartsWith("business.") || tag.Key.StartsWith("user."))
            {
                context[$"parent.{tag.Key}"] = tag.Value ?? "unknown";
            }
        }

        if (!string.IsNullOrEmpty(activity.OperationName))
        {
            context["parent.operation_name"] = activity.OperationName;
        }
    }
}
