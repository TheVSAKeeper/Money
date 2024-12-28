using Microsoft.AspNetCore.Components;
using Money.Web2.Common;
using Money.Web2.Services.Authentication;

namespace Money.Web2.Pages.Account;

public partial class Logout
{
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; } = null;

    [Inject]
    private AuthenticationService AuthenticationService { get; set; } = null!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await AuthenticationService.LogoutAsync();
        NavigationManager.ReturnTo(ReturnUrl, true);
    }
}
