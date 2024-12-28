using Money.Web2.Models;

namespace Money.Web2.Common;

public class OperationSearchEventArgs : EventArgs
{
    public List<Operation>? Operations { get; init; }
    public bool AddZeroDays { get; init; }
    public bool ShouldRender { get; init; } = true;
}
