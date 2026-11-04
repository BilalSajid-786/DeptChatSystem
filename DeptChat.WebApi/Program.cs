using System.Text;
using DeptChat.Application.Common.Interfaces;
using DeptChat.Application.Services;
using DeptChat.Infrastructure.Persistence;
using DeptChat.Infrastructure.Services;
using DeptChat.WebApi.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// 1. DATABASE & PERSISTENCE (SQL Server SQLEXPRESS)
// =============================================================================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// =============================================================================
// 2. REDIS / IN-MEMORY PRESENCE (Presence Tracking & SignalR Backplane)
// =============================================================================
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
IConnectionMultiplexer? redisMultiplexer = null;

if (!string.IsNullOrEmpty(redisConnectionString))
{
    try
    {
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectTimeout = 1500;
        var mux = ConnectionMultiplexer.Connect(redisOptions);
        if (mux.IsConnected)
        {
            redisMultiplexer = mux;
            Console.WriteLine("[Redis] Successfully connected to Redis at " + redisConnectionString);
        }
        else
        {
            Console.WriteLine("[Redis] Redis not reachable. Falling back to InMemory presence.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Redis] Connection failed ({ex.Message}). Falling back to InMemory presence.");
    }
}

// =============================================================================
// 3. SIGNALR REAL-TIME ENGINE
// =============================================================================
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB for voice notes & attachments
    options.EnableDetailedErrors = true;
});

if (redisMultiplexer != null)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
    signalRBuilder.AddStackExchangeRedis(redisConnectionString!, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("DeptChat_SignalR");
    });
}

// =============================================================================
// 4. AUTHENTICATION & JWT (Configured for both HTTP & WebSockets)
// =============================================================================
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSettings["Secret"] ?? "A_VERY_LONG_SUPER_SECRET_KEY_FOR_JWT_AUTHENTICATION_1234567890");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(secretKey)
    };

    // Enables JWT Authentication over SignalR WebSockets query string
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// =============================================================================
// 5. APPLICATION SERVICES (Auth, Password Hasher, Tokens)
// =============================================================================
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
if (redisMultiplexer != null)
{
    builder.Services.AddSingleton<IPresenceService, RedisPresenceService>();
}
else
{
    builder.Services.AddSingleton<IPresenceService, InMemoryPresenceService>();
}
builder.Services.AddSingleton<IPushNotificationService, FirebasePushNotificationService>();

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// =============================================================================
// 6. SWAGGER / OPENAPI SPECIFICATION WITH JWT BEARER AUTH
// =============================================================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Eagerly initialize Push Notification Service so Firebase credentials are verified on startup
try
{
    app.Services.GetRequiredService<IPushNotificationService>();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Failed to eagerly initialize IPushNotificationService on startup.");
}

// =============================================================================
// 7. DATABASE INITIALIZATION (Ensures DB, Tables, Indexes, and Seeds Exist)
// =============================================================================
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Wait for SQL Server to accept connections with exponential retry
    int retries = 15;
    while (retries > 0)
    {
        try
        {
            Console.WriteLine("[Database] Checking SQL Server connection and applying seed...");
            dbContext.Database.EnsureCreated();
            Console.WriteLine("[Database] SQL Server is ready and database initialized successfully!");
            break;
        }
        catch (Exception ex)
        {
            retries--;
            Console.WriteLine($"[Database] SQL Server not ready yet ({ex.Message}). Retries remaining: {retries}. Waiting 3s...");
            Thread.Sleep(3000);
            if (retries == 0) throw;
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "DeptChat API v1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
