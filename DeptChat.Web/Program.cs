using Yarp.ReverseProxy.Configuration;

// DeptChat web host.
// - Serves the Blazor WebAssembly client (DeptChat.Web.Client).
// - Forwards /api, /hubs (SignalR, incl. WebSockets) and /uploads to the existing backend,
//   so the browser talks to one origin and the backend needs no CORS or code changes.
var builder = WebApplication.CreateBuilder(args);

var backendUrl = builder.Configuration["Backend:BaseUrl"];
if (string.IsNullOrWhiteSpace(backendUrl))
{
    backendUrl = "http://localhost:5051";
}

var routes = new[]
{
    new RouteConfig { RouteId = "api", ClusterId = "backend", Match = new RouteMatch { Path = "/api/{**catch-all}" } },
    new RouteConfig { RouteId = "hubs", ClusterId = "backend", Match = new RouteMatch { Path = "/hubs/{**catch-all}" } },
    new RouteConfig { RouteId = "uploads", ClusterId = "backend", Match = new RouteMatch { Path = "/uploads/{**catch-all}" } },
};

var clusters = new[]
{
    new ClusterConfig
    {
        ClusterId = "backend",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["primary"] = new DestinationConfig { Address = backendUrl }
        }
    }
};

builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);

var app = builder.Build();

app.Logger.LogInformation("DeptChat web forwarding /api, /hubs and /uploads to {Backend}", backendUrl);

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapReverseProxy();
app.MapFallbackToFile("index.html");

app.Run();
