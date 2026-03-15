using Timer = System.Timers.Timer;
using static Money.ApiClient.AdminClient;

namespace Money.Web.Pages.Admin;

public sealed partial class ShardingPanel
{
    private static readonly ChartOptions PartitionChartOptions = new()
    {
        YAxisFormat = "#,##0",
    };

    private ShardsMetricsResponse? _response;
    private List<UserShardInfo>? _users;
    private string _userSearch = "";
    private Timer? _timer;
    private string[] _xAxisLabels = [];
    private double[] _rowsData = [];
    private double[] _sizeData = [];
    private long _totalRows;
    private long _totalDbSize;

    private List<PartitionListResponse>? _partitions;
    private string _selectedPartitionShard = "";
    private List<ChartSeries> _partitionChartSeries = [];
    private string[] _partitionMonthLabels = [];

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
            _xAxisLabels = shards.Select(x => x.Key).ToArray();
            _rowsData = shards.Select(x => (double)x.Value.TotalRows).ToArray();
            _sizeData = shards.Select(x => (double)x.Value.DbSizeBytes).ToArray();
            _totalRows = shards.Sum(x => x.Value.TotalRows);
            _totalDbSize = shards.Sum(x => x.Value.DbSizeBytes);
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
            _partitionMonthLabels = [];
            _partitionChartSeries = [];
            return;
        }

        var sorted = shardData.Partitions.OrderBy(p => p.Name).ToArray();

        _partitionMonthLabels = sorted
            .Select(p => $"{p.RangeStart.Year}-{p.RangeStart.Month:D2}")
            .ToArray();

        _partitionChartSeries =
        [
            new()
            {
                Name = "Размер (байт)",
                Data = sorted.Select(p => (double)p.SizeBytes).ToArray(),
            },
        ];
    }

    private void OnPartitionShardChanged(string shard)
    {
        _selectedPartitionShard = shard;
        UpdatePartitionChart();
    }
}
