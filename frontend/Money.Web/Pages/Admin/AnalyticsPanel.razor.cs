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

    private ClickHouseStatsResponse? _stats;
    private List<ApiMetricsPerMinute>? _metricsPerMinute;
    private List<SlowEndpointInfo>? _slowEndpoints;
    private Chart? _chart;

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

    private async Task LoadAllAsync()
    {
        await Task.WhenAll(LoadStatsAsync(), LoadMetricsAsync(), LoadSlowEndpointsAsync());
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
}
