namespace DeptChat.Shared.DTOs;

/// <summary>
/// A problem report sent automatically by the app (crash, freeze, network error...).
/// Channel: "internal" (test builds) or "live" (customer builds).
/// Kind: crash, freeze, network, error or other.
/// Username, tenant and the login session's device are added by the server.
/// </summary>
public record ErrorReportDto(
    string Channel,
    string Kind,
    string Content,
    string? AppVersion = null,          // e.g. "1.0.11"
    string? AppBuild = null,            // e.g. "13"
    string? TestLabel = null,           // e.g. "RC1" (test builds only)
    string? DeviceManufacturer = null,  // e.g. "Xiaomi"
    string? DeviceModel = null,         // e.g. "Redmi Note 12"
    string? OsVersion = null,           // e.g. "Android 13 (API 33)"
    string? NetworkType = null,         // e.g. "WiFi", "Mobile", "None"
    string? DeviceDetails = null,       // anything else, e.g. "RAM 4 GB, 64-bit"
    DateTime? OccurredAtUtc = null
);

public record ErrorReportResultDto(string ReportId);
