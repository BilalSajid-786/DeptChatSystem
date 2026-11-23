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
app.UseStaticFiles(new StaticFileOptions
{
    // index.html, js/deptchat.js and css/app.css keep the same names between releases, so make
    // browsers re-check them (304 if unchanged) instead of running a stale copy after an update.
    // _framework files are fingerprinted and handled by Blazor's own caching.
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) &&
            (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
             path == "/"))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});

app.MapReverseProxy();
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

app.Run();
