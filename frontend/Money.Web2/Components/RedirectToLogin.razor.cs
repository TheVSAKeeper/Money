﻿namespace Money.Web2.Components;

public partial class RedirectToLogin
{
    protected override void OnInitialized()
    {
        NavigationManager.NavigateTo($"Account/Login?returnUrl={Uri.EscapeDataString(NavigationManager.Uri)}", true);
    }
}
