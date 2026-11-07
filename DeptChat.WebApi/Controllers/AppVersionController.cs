using DeptChat.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeptChat.WebApi.Controllers;

[ApiController]
[Route("api/app")]
public class AppVersionController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public AppVersionController(IConfiguration configuration, IWebHostEnvironment env)
    {
        _configuration = configuration;
        _env = env;
    }

    /// <summary>
    /// Returns the current app version, minimum supported version, and update requirements.
    /// Used by mobile clients on startup to determine if an update gate must be displayed.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("version")]
    public ActionResult<AppVersionDto> GetVersion()
    {
        var section = _configuration.GetSection("AppVersion");

        var latestVersion = section["LatestVersion"] ?? "1.0.1";
        var latestBuild = int.TryParse(section["LatestBuildNumber"], out var lb) ? lb : 2;
        var minVersion = section["MinimumRequiredVersion"] ?? "1.0.1";
        var minBuild = int.TryParse(section["MinimumRequiredBuildNumber"], out var mb) ? mb : 2;
        var forceUpdate = bool.TryParse(section["ForceUpdate"], out var fu) ? fu : false;
        var downloadUrl = section["DownloadUrl"] ?? "https://179.197.255.148.sslip.io/api/app/download-apk";
        var releaseNotes = section["ReleaseNotes"] ?? "• Performance improvements and bug fixes.";

        return Ok(new AppVersionDto(
            latestVersion,
            latestBuild,
            minVersion,
            minBuild,
            forceUpdate,
            downloadUrl,
            releaseNotes
        ));
    }

    /// <summary>
    /// Serves or redirects to the latest DeptChat APK for manual sideloading.
    /// If configured with a Google Drive URL, it cleanly redirects the client browser there.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("download-apk")]
    public IActionResult DownloadApk()
    {
        var configuredUrl = _configuration["AppVersion:DownloadUrl"];

        // If configured URL is an external link (like Google Drive), redirect directly
        if (!string.IsNullOrWhiteSpace(configuredUrl) &&
            !configuredUrl.Contains("/api/app/download-apk", StringComparison.OrdinalIgnoreCase))
        {
            return Redirect(configuredUrl);
        }

        // Check wwwroot/apk/DeptChat.apk
        var wwwrootApk = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "apk", "DeptChat.apk");
        if (System.IO.File.Exists(wwwrootApk))
        {
            return PhysicalFile(Path.GetFullPath(wwwrootApk), "application/vnd.android.package-archive", "DeptChat.apk");
        }

        // Check project root DeptChat.apk (F:\Projects\DeptChatSystem\DeptChat.apk)
        var rootApk = Path.Combine(Directory.GetCurrentDirectory(), "..", "DeptChat.apk");
        if (System.IO.File.Exists(rootApk))
        {
            return PhysicalFile(Path.GetFullPath(rootApk), "application/vnd.android.package-archive", "DeptChat.apk");
        }

        return NotFound(new { message = "APK file not found on server." });
    }
}
