using Microsoft.AspNetCore.Components;
using Timer = System.Timers.Timer;
using static Money.ApiClient.AdminClient;

namespace Money.Web.Pages.Admin;

public sealed partial class CachingPanel
{
    private readonly string[] _hitMissLabels = ["Hits", "Misses"];

    private CacheStatsResponse? _stats;
    private List<CacheCounterInfo>? _counters;
    private LockStatsResponse? _lockStats;
    private Timer? _timer;

    private double[] _hitMissData = [];

    private bool _flushing;
    private string? _flushMessage;
    private Severity _flushSeverity = Severity.Success;

    [Inject]
    private NotificationService Notifications { get; set; } = null!;

    public void Dispose()
    {
        Notifications.OnAdminEvent -= HandleAdminEvent;
        _timer?.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        Notifications.OnAdminEvent += HandleAdminEvent;

        await Task.WhenAll(LoadStatsAsync(), LoadCountersAsync(), LoadLockStatsAsync());

        _timer = new(15000);
        _timer.Elapsed += async (_, _) =>
        {
            await Task.WhenAll(LoadStatsAsync(), LoadCountersAsync(), LoadLockStatsAsync());
            await InvokeAsync(StateHasChanged);
        };

        _timer.Start();
    }

    private async void HandleAdminEvent(string eventType, string jsonData)
    {
        if (eventType != "CacheFlushed")
        {
            return;
        }

        await Task.WhenAll(LoadStatsAsync(), LoadCountersAsync(), LoadLockStatsAsync());
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            var result = await Client.Admin.GetCacheStats();

            if (!result.IsSuccessStatusCode || result.Content == null)
            {
                return;
            }

            _stats = result.Content;
            _hitMissData = [_stats.HitsTotal, _stats.MissesTotal];
        }
        catch (Exception ex)
        {
            Client.Log($"Ошибка загрузки статистики кэша: {ex.Message}");
        }
    }

    private async Task LoadCountersAsync()
    {
        try
        {
            var result = await Client.Admin.GetCounters();

            if (result is { IsSuccessStatusCode: true, Content: not null })
            {
                _counters = result.Content;
            }
        }
        catch (Exception ex)
        {
            Client.Log($"Ошибка загрузки счётчиков: {ex.Message}");
        }
    }

    private async Task LoadLockStatsAsync()
    {
        try
        {
            var result = await Client.Admin.GetLockStats();

            if (result is { IsSuccessStatusCode: true, Content: not null })
            {
                _lockStats = result.Content;
            }
        }
        catch (Exception ex)
        {
            Client.Log($"Ошибка загрузки статистики блокировок: {ex.Message}");
        }
    }

    private async Task FlushCacheAsync()
    {
        _flushing = true;
        _flushMessage = null;

        try
        {
            var result = await Client.Admin.FlushCache();

            if (result.IsSuccessStatusCode)
            {
                _flushSeverity = Severity.Success;
                _flushMessage = "Кэш успешно очищен.";
                await Task.WhenAll(LoadStatsAsync(), LoadCountersAsync());
            }
            else
            {
                _flushSeverity = Severity.Error;
                _flushMessage = "Ошибка при очистке кэша. Убедитесь, что у вас есть роль Admin.";
            }
        }
        catch (Exception ex)
        {
            _flushSeverity = Severity.Error;
            _flushMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _flushing = false;
        }
    }
}
