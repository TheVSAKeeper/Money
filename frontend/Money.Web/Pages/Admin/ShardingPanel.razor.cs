using Money.Web.Components.Charts;
using Money.Web.Models.Charts.Config;
using Timer = System.Timers.Timer;
using static Money.ApiClient.AdminClient;

namespace Money.Web.Pages.Admin;

public sealed partial class ShardingPanel
{
    private readonly ChartConfig _rowsChartConfig = new()
    {
        Type = "doughnut",
        Options = new()
        {
            Responsive = true,
            Plugins = new()
            {
                Legend = new()
                {
                    Display = true,
                    Position = "right",
                },
            },
        },
    };

    private readonly ChartConfig _sizeChartConfig = new()
    {
        Type = "doughnut",
        Options = new()
        {
            Responsive = true,
            Plugins = new()
            {
                Legend = new()
                {
                    Display = true,
                    Position = "right",
                },
            },
        },
    };

    private readonly ChartConfig _partitionChartConfig = new()
    {
        Type = "bar",
        Options = new()
        {
            Responsive = true,
            Plugins = new()
            {
                Legend = new()
                {
                    Display = false,
                },
            },
        },
    };

    private ShardsMetricsResponse? _response;
    private List<UserShardInfo>? _users;
    private string _userSearch = "";
    private Timer? _timer;
    private long _totalRows;
    private long _totalDbSize;
    private Chart? _rowsChart;
    private Chart? _sizeChart;

    private List<PartitionListResponse>? _partitions;
    private string _selectedPartitionShard = "";
    private Chart? _partitionChart;

    private PartitionListResponse? SelectedShardPartitions => _partitions?.FirstOrDefault(p => p.Shard == _selectedPartitionShard);

    public void Dispose()
    {
        _timer?.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(LoadDataAsync(), LoadUsersAsync(), LoadPartitionsAsync());

        _timer = new(30000);
        _timer.Elapsed += async (_, _) =>
        {
            await Task.WhenAll(LoadDataAsync(), LoadUsersAsync(), LoadPartitionsAsync());
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

    private async Task LoadDataAsync()
    {
        try
        {
            var result = await Client.Admin.GetShardsMetrics();

            if (!result.IsSuccessStatusCode || result.Content == null)
            {
                return;
            }

            _response = result.Content;

            var shards = _response.Shards.OrderBy(x => x.Key).ToArray();
            var labels = shards.Select(x => x.Key).ToList();

            _rowsChartConfig.Data.Labels = labels;
            _rowsChartConfig.Data.Datasets.Clear();
            _rowsChartConfig.Data.Datasets.Add(new()
            {
                BackgroundColor = shards.Select((_, i) => (object)ChartColors.GetColor(i)).ToArray(),
                Data = shards.Select(x => (decimal?)x.Value.TotalRows).ToList(),
            });

            _sizeChartConfig.Data.Labels = labels;
            _sizeChartConfig.Data.Datasets.Clear();
            _sizeChartConfig.Data.Datasets.Add(new()
            {
                BackgroundColor = shards.Select((_, i) => (object)ChartColors.GetColor(i)).ToArray(),
                Data = shards.Select(x => (decimal?)x.Value.DbSizeBytes).ToList(),
            });

            _totalRows = shards.Sum(x => x.Value.TotalRows);
            _totalDbSize = shards.Sum(x => x.Value.DbSizeBytes);

            if (_rowsChart != null)
            {
                await _rowsChart.UpdateAsync();
            }

            if (_sizeChart != null)
            {
                await _sizeChart.UpdateAsync();
            }
        }
        catch (Exception ex)
        {
            Client.Log($"Ошибка загрузки метрик шардов: {ex.Message}");
        }
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var result = await Client.Admin.GetUserShards();

            if (result.IsSuccessStatusCode && result.Content != null)
            {
                _users = result.Content;
            }
        }
        catch (Exception ex)
        {
            Client.Log($"Ошибка загрузки пользователей: {ex.Message}");
        }
    }

    private bool FilterUser(UserShardInfo u)
    {
        return string.IsNullOrWhiteSpace(_userSearch)
               || u.UserName.Contains(_userSearch, StringComparison.OrdinalIgnoreCase)
               || u.Email.Contains(_userSearch, StringComparison.OrdinalIgnoreCase)
               || u.ShardName.Contains(_userSearch, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadPartitionsAsync()
    {
        try
        {
            var result = await Client.Admin.GetPartitions();

            if (!result.IsSuccessStatusCode || result.Content == null)
            {
                return;
            }

            _partitions = result.Content;

            if (string.IsNullOrEmpty(_selectedPartitionShard) || _partitions.All(p => p.Shard != _selectedPartitionShard))
            {
                _selectedPartitionShard = _partitions.FirstOrDefault()?.Shard ?? "";
            }

            UpdatePartitionChart();
        }
        catch (Exception ex)
        {
            Client.Log($"Ошибка загрузки партиций: {ex.Message}");
        }
    }

    private void UpdatePartitionChart()
    {
        var shardData = SelectedShardPartitions;

        if (shardData == null)
        {
            _partitionChartConfig.Data.Labels.Clear();
            _partitionChartConfig.Data.Datasets.Clear();
            _ = _partitionChart?.UpdateAsync();
            return;
        }

        var sorted = shardData.Partitions.OrderBy(p => p.Name).ToArray();

        _partitionChartConfig.Data.Labels = sorted
            .Select(p => $"{p.RangeStart.Year}-{p.RangeStart.Month:D2}")
            .ToList();

        _partitionChartConfig.Data.Datasets.Clear();
        _partitionChartConfig.Data.Datasets.Add(new()
        {
            Label = "Размер (байт)",
            BackgroundColor = ChartColors.GetColor(0),
            Data = sorted.Select(p => (decimal?)p.SizeBytes).ToList(),
        });

        _ = _partitionChart?.UpdateAsync();
    }

    private void OnPartitionShardChanged(string shard)
    {
        _selectedPartitionShard = shard;
        UpdatePartitionChart();
    }
}
