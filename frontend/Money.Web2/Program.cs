using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using Money.ApiClient;
using Money.Web2.Services;
using Money.Web2.Services.Authentication;
using Money.WebAssembly.CoreLib;
using NCalc.DependencyInjection;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
Uri apiUri = new("https+http://api/");

builder.AddServiceDefaults();
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();

builder.Services.AddMemoryCache();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddNCalc();
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<FastOperationService>();
builder.Services.AddScoped<PlaceService>();
builder.Services.AddTransient<RefreshTokenHandler>();

builder.Services.AddHttpClient<AuthenticationService>(client => client.BaseAddress = apiUri);
builder.Services.AddHttpClient<JwtParser>(client => client.BaseAddress = apiUri);

builder.Services.AddHttpClient<MoneyClient>(client => client.BaseAddress = apiUri)
    .AddHttpMessageHandler<RefreshTokenHandler>();

await builder.Build().RunAsync();
