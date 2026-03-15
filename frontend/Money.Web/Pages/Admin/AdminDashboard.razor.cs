using Microsoft.AspNetCore.Components;

namespace Money.Web.Pages.Admin;

public partial class AdminDashboard
{
    [Inject]
    public required NavigationManager Nav { get; set; }
}
