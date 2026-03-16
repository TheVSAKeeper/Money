using Money.ApiClient;
using System.Text.Json;

namespace Money.Web.Pages.Admin;

public sealed partial class EmailQueuePanel
{
    private AdminClient.EmailQueueStatsResponse? _stats;
    private bool _simulating;

    public void Dispose()
    {
        Notifications.OnAdminEvent -= HandleAdminEvent;
    }

    protected override async Task OnInitializedAsync()
    {
        Notifications.OnAdminEvent += HandleAdminEvent;
        await LoadDataAsync();
    }

    private async void HandleAdminEvent(string eventType, string jsonData)
    {
        if (eventType != "EmailQueueChanged")
        {
            return;
        }

        try
        {
            var doc = JsonDocument.Parse(jsonData);
            var stats = doc.RootElement.GetProperty("stats");
            var newDlq = stats.GetProperty("dlqLen").GetInt64();
            var newRetry = stats.GetProperty("retryLen").GetInt64();

            var prevDlq = _stats?.DlqLength ?? 0;
            var prevRetry = _stats?.RetryLength ?? 0;

            if (_stats != null)
            {
                _stats.QueueLength = stats.GetProperty("queueLen").GetInt64();
                _stats.RetryLength = newRetry;
                _stats.DlqLength = newDlq;
            }

            if (newDlq > prevDlq || newRetry > prevRetry)
            {
                await LoadDataAsync();
            }

            await InvokeAsync(StateHasChanged);
        }
        catch
        {
        }
    }

    private async Task LoadDataAsync()
    {
        var result = await Client.Admin.GetEmailQueueStatsAsync();

        if (result.Content != null)
        {
            _stats = result.Content;
        }
    }

    private async Task SimulateAsync()
    {
        _simulating = true;

        try
        {
            await Client.Admin.SimulateEmailQueueAsync();
            await LoadDataAsync();
        }
        finally
        {
            _simulating = false;
        }
    }
}
