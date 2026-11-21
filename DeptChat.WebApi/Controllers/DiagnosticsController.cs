using System.Security.Claims;
using System.Text;
using DeptChat.Shared.DTOs;
using DeptChat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DeptChat.WebApi.Controllers;

/// <summary>
/// Receives problem reports sent automatically by the app and saves each one as a text file:
///   {ErrorReports:RootPath}/internaltesterrors/yyyy-MM-dd/...txt   (test builds)
///   {ErrorReports:RootPath}/livecustomererrors/yyyy-MM-dd/...txt  (customer builds)
/// The folder is outside wwwroot, so reports are never downloadable over HTTP.
/// In Docker it is the "error_reports" volume, so reports survive redeploys.
/// </summary>
[Authorize]
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private const int MaxContentChars = 2_000_000;
    private const int MaxReportsPerUserPerHour = 30;

    private static readonly Dictionary<string, string> ChannelFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["internal"] = "internaltesterrors",
        ["live"] = "livecustomererrors"
    };

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "crash", "freeze", "network", "error", "other"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(ApplicationDbContext dbContext, IConfiguration configuration, IMemoryCache memoryCache, ILogger<DiagnosticsController> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return claim != null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string GetUserRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";

    private Guid GetClientId()
    {
        var claim = User.FindFirstValue("client_id");
        return claim != null && Guid.TryParse(claim, out var cid) ? cid : Guid.Empty;
    }

    [HttpPost("error-reports")]
    [RequestSizeLimit(5 * 1024 * 1024)] // 5 MB
    public async Task<IActionResult> SubmitErrorReport([FromBody] ErrorReportDto? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { message = "Report content is required." });
        }

        if (request.Content.Length > MaxContentChars)
        {
            return BadRequest(new { message = "Report is too large." });
        }

        if (string.IsNullOrWhiteSpace(request.Channel) || !ChannelFolders.TryGetValue(request.Channel.Trim(), out var channelFolder))
        {
            return BadRequest(new { message = "Channel must be 'internal' or 'live'." });
        }

        var kind = (request.Kind ?? "").Trim().ToLowerInvariant();
        if (!AllowedKinds.Contains(kind))
        {
            kind = "other";
        }

        var userId = GetUserId();
        var now = DateTime.UtcNow;

        // Simple per-user limit so a misbehaving app cannot fill the disk.
        var rateKey = $"error_reports_{userId}_{now:yyyyMMddHH}";
        var count = _memoryCache.GetOrCreate(rateKey, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return new int[1];
        })!;
        if (Interlocked.Increment(ref count[0]) > MaxReportsPerUserPerHour)
        {
            return StatusCode(429, new { message = "Too many reports. Please try again later." });
        }

        try
        {
            var root = _configuration["ErrorReports:RootPath"];
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(AppContext.BaseDirectory, "error-reports");
            }

            var dayDir = Path.Combine(root, channelFolder, now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dayDir);

            var clientId = GetClientId();
            var fullName = User.Identity?.Name ?? "unknown";

            // Username (or the email/phone they log in with) comes from the database, not from the app.
            var account = await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.ClientId == clientId)
                .Select(u => new { u.Username, u.Email, u.PhoneNumber })
                .FirstOrDefaultAsync();
            var login = account == null ? ""
                : !string.IsNullOrWhiteSpace(account.Username) ? account.Username
                : !string.IsNullOrWhiteSpace(account.Email) ? account.Email
                : account.PhoneNumber ?? "";

            // The device this login session was created on (stored by the server at login).
            var sessionText = "";
            if (Guid.TryParse(User.FindFirstValue("session_id"), out var sessionId))
            {
                var session = await _dbContext.UserSessions.AsNoTracking()
                    .Where(s => s.Id == sessionId && s.UserId == userId)
                    .Select(s => new { s.DeviceName, s.DevicePlatform, s.DeviceId, s.CreatedAt })
                    .FirstOrDefaultAsync();
                if (session != null)
                {
                    sessionText = $"{session.DeviceName} / {session.DevicePlatform} / device id {session.DeviceId} / logged in {session.CreatedAt:yyyy-MM-dd HH:mm} UTC";
                }
            }

            var appText = string.IsNullOrWhiteSpace(request.AppVersion) ? "" : request.AppVersion.Trim();
            if (!string.IsNullOrWhiteSpace(request.AppBuild)) appText += $" (build {request.AppBuild.Trim()})";
            if (!string.IsNullOrWhiteSpace(request.TestLabel)) appText += $" test {request.TestLabel.Trim()}";

            var deviceText = string.Join(", ", new[]
            {
                $"{request.DeviceManufacturer} {request.DeviceModel}".Trim(),
                request.OsVersion ?? "",
                string.IsNullOrWhiteSpace(request.NetworkType) ? "" : $"network {request.NetworkType}",
                request.DeviceDetails ?? ""
            }.Select(p => p.Trim()).Where(p => p.Length > 0));

            var reportId = $"{now:HHmmss}_{kind}_{SafeFilePart(login.Length > 0 ? login : fullName)}_{Guid.NewGuid().ToString("N")[..8]}";
            var filePath = Path.Combine(dayDir, reportId + ".txt");

            var sb = new StringBuilder();
            sb.AppendLine("=== DeptChat error report ===");
            sb.AppendLine($"Received (UTC): {now:yyyy-MM-dd HH:mm:ss}");
            if (request.OccurredAtUtc.HasValue)
            {
                sb.AppendLine($"Occurred (UTC): {request.OccurredAtUtc.Value.ToUniversalTime():yyyy-MM-dd HH:mm:ss}");
            }
            sb.AppendLine($"Channel: {channelFolder}");
            sb.AppendLine($"Kind: {kind}");
            sb.AppendLine($"User: {OneLine(fullName)}");
            sb.AppendLine($"Username: {OneLine(login)}");
            sb.AppendLine($"User id: {userId}");
            sb.AppendLine($"Role: {OneLine(GetUserRole())}");
            sb.AppendLine($"Tenant: {OneLine(User.FindFirstValue("client_name") ?? "")} ({clientId})");
            sb.AppendLine($"App: {OneLine(appText)}");
            sb.AppendLine($"Device (from app): {OneLine(deviceText)}");
            sb.AppendLine($"Device (login session): {OneLine(sessionText)}");
            sb.AppendLine("=============================");
            sb.AppendLine();
            sb.Append(request.Content);

            await System.IO.File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(false));

            _logger.LogInformation("Error report {ReportId} saved to {Folder} ({Chars} chars)", reportId, channelFolder, request.Content.Length);
            return Ok(new ErrorReportResultDto(reportId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save error report");
            return StatusCode(500, new { message = "An error occurred while saving the report." });
        }
    }

    private static string OneLine(string value)
    {
        var s = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length > 200 ? s[..200] : s;
    }

    private static string SafeFilePart(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if ((c == ' ' || c == '.' || c == '-' || c == '_') && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            if (sb.Length >= 30) break;
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "user" : s;
    }
}
