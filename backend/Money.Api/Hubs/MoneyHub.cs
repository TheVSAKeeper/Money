using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Money.Api.Hubs;

[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public class MoneyHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var group = GetUserGroup();
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        // TODO: Role="Admin"
        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var group = GetUserGroup();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admin");
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string GetUserGroup()
    {
        var authUserId = Context.User?.FindFirst(OpenIddictConstants.Claims.Subject)?.Value
                         ?? throw new HubException("User not authenticated");

        return $"user:{authUserId}";
    }
}
