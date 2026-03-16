using Blazored.LocalStorage;
using Microsoft.AspNetCore.SignalR.Client;
using Money.Web.Services.Authentication;
using System.Text.Json;

namespace Money.Web.Services;

public class NotificationService(ILocalStorageService localStorage) : IAsyncDisposable
{
    private HubConnection? _hub;
    public event Action<string, string>? OnAdminEvent; // eventType, jsonData

    public event Action<string, string>? OnNotify;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public async Task StartAsync(string baseUrl)
    {
        if (_hub != null)
        {
            return;
        }

        _hub = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/money", opts =>
            {
                opts.AccessTokenProvider = async () => await localStorage.GetItemAsync<string>(AuthenticationService.AccessTokenKey);
            })
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)])
            .Build();

        _hub.On<string>("Notify", message =>
        {
            try
            {
                var doc = JsonDocument.Parse(message);
                var eventType = doc.RootElement.GetProperty("type").GetString()!;
                OnNotify?.Invoke(eventType, message);
            }
            catch
            {
            }
        });

        _hub.On<string>("AdminEvent", message =>
        {
            try
            {
                var doc = JsonDocument.Parse(message);
                var eventType = doc.RootElement.GetProperty("type").GetString()!;
                OnAdminEvent?.Invoke(eventType, message);
            }
            catch
            {
            }
        });

        await _hub.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_hub != null)
        {
            await _hub.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
        }
    }
}
