using DeptChat.Web.Client;
using DeptChat.Web.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same origin as the page: the DeptChat.Web host forwards /api, /hubs and /uploads to the backend.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// In Blazor WebAssembly "scoped" lives for the whole tab, like the mobile app's singletons.
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ChatHubClient>();
builder.Services.AddScoped<OutboxStore>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<BrowserInterop>();

var host = builder.Build();

await host.Services.GetRequiredService<AuthSession>().InitializeAsync();

await host.RunAsync();
