using Money.ApiClient;

namespace Money.Web.Pages.Admin;

public partial class LakehousePanel : IDisposable
{
    private AdminClient.LakehouseStatsResponse? _stats;
    private bool _loading;
    private bool _actionLoading;
    private string? _error;
    private string? _actionSuccess;
    private string? _currentAction;

    private bool _queryLoading;
    private string? _queryError;
    private List<Dictionary<string, object?>>? _queryResults;
    private List<string> _queryColumns = [];

    private const string FederatedQuery =
        """
        SELECT u.user_name, ms.month, ms.total_sum
        FROM iceberg.gold.monthly_spending ms
        JOIN money_routing.public.aspnetusers u
            ON ms.auth_user_id = CAST(u.id AS VARCHAR)
        ORDER BY ms.month DESC
        LIMIT 20
        """;

    private string _queryEngine = "trino";
    private string _customSql = "";
    private string? _lastExecutedSql;

    private Timer? _refreshTimer;

    protected override Task OnInitializedAsync()
    {
        _refreshTimer = new Timer(_ => InvokeAsync(LoadDataAsync), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        return LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _loading = _stats == null;
        _error = null;
        StateHasChanged();

        try
        {
            var response = await Client.Admin.GetLakehouseStats();

            if (response.Content != null)
            {
                _stats = response.Content;
            }
            else
            {
                _error = "Не удалось загрузить статистику Lakehouse";
            }
        }
        catch (Exception ex)
        {
            _error = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task ForceSync()
    {
        await ExecuteActionAsync(
            "sync",
            () => Client.Admin.ForceLakehouseSync(),
            "Синхронизация запущена");
    }

    private async Task ForceTransform()
    {
        await ExecuteActionAsync(
            "transform",
            () => Client.Admin.ForceLakehouseTransform(),
            "Трансформация запущена");
    }

    private async Task ForceReconcile()
    {
        await ExecuteActionAsync(
            "reconcile",
            () => Client.Admin.ForceLakehouseReconciliation(),
            "Сверка запущена");
    }

    private async Task ExecuteActionAsync(string actionName, Func<Task<ApiClientResponse>> action, string successMessage)
    {
        _actionLoading = true;
        _currentAction = actionName;
        _actionSuccess = null;
        _error = null;
        StateHasChanged();

        try
        {
            await action();
            _actionSuccess = successMessage;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _error = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _actionLoading = false;
            _currentAction = null;
            StateHasChanged();
        }
    }

    private Task RunPresetQuery(string sql)
    {
        _customSql = sql;
        return RunQuery(sql);
    }

    private Task RunCustomQuery()
    {
        return string.IsNullOrWhiteSpace(_customSql) ? Task.CompletedTask : RunQuery(_customSql);
    }

    private async Task RunQuery(string sql)
    {
        _queryLoading = true;
        _queryError = null;
        _queryResults = null;
        _queryColumns = [];
        _lastExecutedSql = sql;
        StateHasChanged();

        try
        {
            var response = _queryEngine == "duckdb"
                ? await Client.Admin.QueryLakehouseDuckDb(sql)
                : await Client.Admin.QueryLakehouseTrino(sql);

            if (!response.IsSuccessStatusCode)
            {
                _queryError = response.GetError()?.Title ?? "Ошибка запроса";
            }
            else if (response.Content is { Count: > 0 })
            {
                _queryResults = response.Content;
                _queryColumns = response.Content[0].Keys.ToList();
            }
            else
            {
                _queryResults = [];
            }
        }
        catch (Exception ex)
        {
            _queryError = ex.Message;
        }
        finally
        {
            _queryLoading = false;
            StateHasChanged();
        }
    }

    private static Color GetLayerColor(string layerName)
    {
        return layerName switch
        {
            "bronze" => Color.Warning,
            "silver" => Color.Default,
            "gold" => Color.Success,
            _ => Color.Info,
        };
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F2} MB",
            >= 1024 => $"{bytes / 1024.0:F2} KB",
            _ => $"{bytes} B",
        };
    }

    private static string FormatTimeAgo(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.UtcNow - time;

        return elapsed.TotalMinutes switch
        {
            < 1 => "только что",
            < 60 => $"{(int)elapsed.TotalMinutes} мин. назад",
            < 1440 => $"{(int)elapsed.TotalHours} ч. назад",
            _ => $"{(int)elapsed.TotalDays} дн. назад",
        };
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}
