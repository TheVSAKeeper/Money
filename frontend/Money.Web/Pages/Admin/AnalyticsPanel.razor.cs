using Money.ApiClient;
using Money.Web.Components.Charts;
using Money.Web.Models.Charts.Config;
using static Money.ApiClient.AdminClient;
using Timer = System.Timers.Timer;

namespace Money.Web.Pages.Admin;

public sealed partial class AnalyticsPanel(MoneyClient client) : IDisposable
{
    private readonly ChartConfig _chartConfig = new()
    {
        Type = "line",
        Options = new()
        {
            Responsive = true,
            Plugins = new()
            {
                Legend = new()
                    { Display = false },
            },
        },
    };

    private readonly ChartConfig _trendsChartConfig = new()
    {
        Type = "line",
        Options = new()
        {
            Responsive = true,
            Plugins = new()
            {
                Legend = new()
                {
                    Display = true,
                    Position = "top",
                },
            },
        },
    };

    private ClickHouseStatsResponse? _stats;
    private List<ApiMetricsPerMinute>? _metricsPerMinute;
    private List<SlowEndpointInfo>? _slowEndpoints;
    private Chart? _chart;

    private CubeResultSet? _cubeExpenses;
    private CubeResultSet? _cubeDebts;
    private CubeResultSet? _cubeTrends;
    private Chart? _trendsChart;
    private int _olapTab;

    private Timer? _timer;

    public void Dispose()
    {
        _timer?.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadAllAsync();

        _timer = new(30_000);

        _timer.Elapsed += async (_, _) =>
        {
            await LoadAllAsync();
            await InvokeAsync(StateHasChanged);
        };

        _timer.Start();
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffix = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        int i;
        var dblSByte = (double)bytes;

        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return $"{dblSByte:0.##} {suffix[i]}";
    }

    private static string GetValue(Dictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out var val) ? val?.ToString() ?? "" : "";
    }

    private async Task LoadAllAsync()
    {
        await Task.WhenAll(LoadStatsAsync(),
            LoadMetricsAsync(),
            LoadSlowEndpointsAsync(),
            LoadCubeExpensesAsync(),
            LoadCubeDebtsAsync(),
            LoadCubeTrendsAsync());
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            var result = await client.Admin.GetClickHouseStats();

            if (result is { IsSuccessStatusCode: true, Content: not null })
            {
                _stats = result.Content;
            }
        }
        catch (Exception ex)
        {
            client.Log($"Ошибка загрузки ClickHouse Stats: {ex.Message}");
        }
    }

    private async Task LoadMetricsAsync()
    {
        try
        {
            var result = await client.Admin.GetApiMetricsPerMinute();

            if (!result.IsSuccessStatusCode || result.Content == null)
            {
                return;
            }

            _metricsPerMinute = result.Content;

            _chartConfig.Data.Labels = _metricsPerMinute
                .Select(m => m.Minute.ToString("HH:mm"))
                .ToList();

            _chartConfig.Data.Datasets.Clear();
            _chartConfig.Data.Datasets.Add(new()
            {
                Label = "Запросов/мин",
                BackgroundColor = ChartColors.GetColor(0),
                Data = _metricsPerMinute.Select(m => (decimal?)m.Requests).ToList(),
            });

            if (_chart != null)
            {
                await _chart.UpdateAsync();
            }
        }
        catch (Exception ex)
        {
            client.Log($"Ошибка загрузки API metrics: {ex.Message}");
        }
    }

    private async Task LoadSlowEndpointsAsync()
    {
        try
        {
            var result = await client.Admin.GetSlowEndpoints();

            if (result is { IsSuccessStatusCode: true, Content: not null })
            {
                _slowEndpoints = result.Content;
            }
        }
        catch (Exception ex)
        {
            client.Log($"Ошибка загрузки slow endpoints: {ex.Message}");
        }
    }

    private async Task LoadCubeExpensesAsync()
    {
        try
        {
            var result = await client.Admin.GetCubeExpenses();

            if (result is { IsSuccessStatusCode: true, Content: not null })
            {
                _cubeExpenses = result.Content;
            }
        }
        catch (Exception ex)
        {
            client.Log($"Ошибка загрузки Cube расходов: {ex.Message}");
        }
    }

    private async Task LoadCubeDebtsAsync()
    {
        try
        {
            var result = await client.Admin.GetCubeDebts();

            if (result is { IsSuccessStatusCode: true, Content: not null })
            {
                _cubeDebts = result.Content;
            }
        }
        catch (Exception ex)
        {
            client.Log($"Ошибка загрузки Cube долгов: {ex.Message}");
        }
    }

    private async Task LoadCubeTrendsAsync()
    {
        try
        {
            var result = await client.Admin.GetCubeTrends();

            if (!result.IsSuccessStatusCode || result.Content == null)
            {
                return;
            }

            _cubeTrends = result.Content;

            var labels = _cubeTrends.Data
                .Select(row => GetValue(row, "operations.date.week") is { Length: > 0 } v ? v : GetValue(row, "operations.date"))
                .ToList();

            var expenseRows = _cubeTrends.Data
                .Where(r => GetValue(r, "operations.operation_type") == "1")
                .ToList();

            var incomeRows = _cubeTrends.Data
                .Where(r => GetValue(r, "operations.operation_type") == "2")
                .ToList();

            _trendsChartConfig.Data.Labels = labels.Distinct().ToList();
            _trendsChartConfig.Data.Datasets.Clear();
            _trendsChartConfig.Data.Datasets.Add(new()
            {
                Label = "Расходы",
                BackgroundColor = ChartColors.GetColor(0),
                Data = expenseRows.Select(r => decimal.TryParse(GetValue(r, "operations.total_sum"), out var v) ? (decimal?)v : null).ToList(),
            });

            _trendsChartConfig.Data.Datasets.Add(new()
            {
                Label = "Доходы",
                BackgroundColor = ChartColors.GetColor(1),
                Data = incomeRows.Select(r => decimal.TryParse(GetValue(r, "operations.total_sum"), out var v) ? (decimal?)v : null).ToList(),
            });

            if (_trendsChart != null)
            {
                await _trendsChart.UpdateAsync();
            }
        }
        catch (Exception ex)
        {
            client.Log($"Ошибка загрузки Cube трендов: {ex.Message}");
        }
    }
}
