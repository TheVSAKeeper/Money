using Money.ApiClient;

namespace Money.Web.Pages.Admin;

public partial class GraphPanel
{
    private GraphMode _mode = GraphMode.Debts;
    private AdminClient.GraphResponse? _graph;
    private bool _loading;
    private string? _error;
    private int _displayLimit = 50;

    private enum GraphMode
    {
        Debts,
        Categories,
    }

    protected override Task OnInitializedAsync()
    {
        return LoadDataAsync();
    }

    private static string FormatProperties(Dictionary<string, object>? props)
    {
        if (props == null || props.Count == 0)
        {
            return "-";
        }

        return string.Join(", ", props.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private Task SwitchMode(GraphMode mode)
    {
        _mode = mode;
        _displayLimit = 50;
        return LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var limit = _mode == GraphMode.Debts ? 200 : 500;
            var response = _mode == GraphMode.Debts
                ? await Client.Admin.GetDebtGraph(limit)
                : await Client.Admin.GetCategoryTree(limit);

            if (response.Content != null)
            {
                _graph = response.Content;
            }
            else
            {
                _error = "Не удалось загрузить данные графа";
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

    private void LoadMore()
    {
        _displayLimit += 50;
    }
}
