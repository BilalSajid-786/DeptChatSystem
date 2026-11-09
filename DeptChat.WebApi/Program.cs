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
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// 1. DATABASE & PERSISTENCE (SQL Server SQLEXPRESS)
// =============================================================================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddMemoryCache();

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
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(90);
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

    // Enables JWT Authentication over SignalR WebSockets query string & validates session / security stamp
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
        },
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            if (principal == null) return;

            var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tokenStamp = principal.FindFirst("security_stamp")?.Value;
            var sessionIdStr = principal.FindFirst("session_id")?.Value;

            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                context.Fail("Invalid user identifier in token.");
                return;
            }

            var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"user_security_stamp_{userId}";

            if (!cache.TryGetValue(cacheKey, out string? currentStamp))
            {
                var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null || user.IsBlocked)
                {
                    context.Fail("User account not found or is blocked.");
                    return;
                }
                currentStamp = user.SecurityStamp ?? string.Empty;
                cache.Set(cacheKey, currentStamp, TimeSpan.FromMinutes(10));
            }

            // If token has security stamp claim, verify it matches
            if (!string.IsNullOrEmpty(tokenStamp) && !string.Equals(currentStamp, tokenStamp, StringComparison.Ordinal))
            {
                context.Fail("Security stamp mismatch. Password has changed or sessions revoked.");
                return;
            }

            // Verify session active state if session_id is present
            if (!string.IsNullOrEmpty(sessionIdStr) && Guid.TryParse(sessionIdStr, out var sessionId))
            {
                var sessionCacheKey = $"user_session_active_{sessionId}";
                if (!cache.TryGetValue(sessionCacheKey, out bool isSessionActive))
                {
                    var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var session = await dbContext.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId);
                    isSessionActive = session != null && session.IsActive;
                    cache.Set(sessionCacheKey, isSessionActive, TimeSpan.FromMinutes(5));
                }

                if (!isSessionActive)
                {
                    context.Fail("Session has been revoked or logged out.");
                    return;
                }
            }
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

            // Safe idempotent migration for SecurityStamp column and UserSessions table
            try
            {
                dbContext.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'SecurityStamp')
                    BEGIN
                        ALTER TABLE Users ADD SecurityStamp NVARCHAR(64) DEFAULT NEWID() NOT NULL;
                    END
                ");

                dbContext.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserSessions')
                    BEGIN
                        CREATE TABLE UserSessions (
                            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                            UserId UNIQUEIDENTIFIER NOT NULL,
                            DeviceName NVARCHAR(150) NOT NULL,
                            DevicePlatform NVARCHAR(50) NOT NULL,
                            DeviceId NVARCHAR(150) NULL,
                            IpAddress NVARCHAR(100) NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            LastActiveAt DATETIME2 NOT NULL,
                            IsActive BIT NOT NULL,
                            RevokedAt DATETIME2 NULL,
                            RevokedReason NVARCHAR(MAX) NULL,
                            CONSTRAINT FK_UserSessions_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
                        );
                        CREATE INDEX IX_UserSessions_UserId ON UserSessions (UserId);
                        CREATE INDEX IX_UserSessions_UserId_IsActive ON UserSessions (UserId, IsActive);
                    END
                ");
            }
            catch (Exception migEx)
            {
                Console.WriteLine($"[Database] Migration verification note: {migEx.Message}");
            }

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
