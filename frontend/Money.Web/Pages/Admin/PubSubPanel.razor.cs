using Money.ApiClient;

namespace Money.Web.Pages.Admin;

public sealed partial class PubSubPanel
{
    private AdminClient.PubSubMetricsResponse? _metrics;
    private Timer? _timer;

    public void Dispose()
    {
        _timer?.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
        _timer = new(async _ =>
        {
            await LoadDataAsync();
            await InvokeAsync(StateHasChanged);
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async Task LoadDataAsync()
    {
        var result = await Client.Admin.GetPubSubMetrics();

        if (result.Content != null)
        {
            _metrics = result.Content;
        }
    }
}
