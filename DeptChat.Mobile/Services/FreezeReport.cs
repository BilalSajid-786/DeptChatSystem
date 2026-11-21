using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DeptChat.Shared.DTOs;

namespace DeptChat.Mobile.Services;

/// <summary>
/// Automatic problem reports (all builds, no popup).
/// Android 11+ keeps a record of why this app stopped, including the full stack trace of every
/// "isn't responding" freeze (ANR). On start, on resume and after login this reads that history plus
/// the errors and crashes recorded by <see cref="DiagLog"/>. New problems become one report that is
/// saved on the phone first and then sent to the server (POST api/diagnostics/error-reports):
/// test builds to the "internal" folder, customer builds to the "live" folder.
/// A report stays on the phone until the server accepts it (not logged in, offline, server down...).
/// Normal background clean-ups by Android ("low memory" / "too many empty" while the app is not
/// on screen) are listed for context but never trigger a report.
/// </summary>
public static class FreezeReport
{
    // New key: earlier builds stored a timestamp that would hide older freezes still kept by Android.
    private const string LastReportedKey = "diag_last_reported_exit_ts_v2";

    // Android "importance" 100 = on screen, 200 = visible; higher = in the background.
    private const int VisibleImportance = 200;

    private const int MaxTracesInReport = 5;
    private const int MaxExitLines = 40;
    private const int MaxContentChars = 1_900_000; // server accepts up to 2,000,000
    private const int MaxPendingReports = 10;
    private const string PendingFolderName = "pending_error_reports";

#if DEPTCHAT_DIAG || DEBUG
    private const string Channel = "internal";
    private const string BuildKind = "internal test build";
#else
    private const string Channel = "live";
    private const string BuildKind = "customer build";
#endif

    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static DateTime _lastRunUtc = DateTime.MinValue;

    private static string PendingDir => Path.Combine(FileSystem.AppDataDirectory, PendingFolderName);

    /// <summary>
    /// Collects new problems into a report and sends every saved report. Never throws, never shows a popup.
    /// </summary>
    /// <param name="delayMs">Wait first, so the screen can appear before any work starts.</param>
    /// <param name="throttle">Skip if a check ran in the last 5 minutes (used on resume).</param>
    public static async Task CheckAndSendAsync(int delayMs = 0, bool throttle = false)
    {
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs);
            if (throttle && DateTime.UtcNow - _lastRunUtc < TimeSpan.FromMinutes(5)) return;
            if (!await RunGate.WaitAsync(0)) return; // a check is already running

            try
            {
                _lastRunUtc = DateTime.UtcNow;

                // Reading Android's exit history and stack traces can take a while: never on the screen thread.
                await Task.Run(QueueNewProblems);
                await SendPendingAsync(); // silent: nothing is shown to the user
            }
            finally
            {
                RunGate.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FreezeReport] {ex.Message}");
        }
    }

    /// <summary>Builds a report from new problems and saves it on the phone until it is sent.</summary>
    private static void QueueNewProblems()
    {
        var report = BuildReport(out var newestTimestamp, out var kind);
        if (report == null) return;

        if (report.Length > MaxContentChars)
        {
            report = report[..MaxContentChars] + Environment.NewLine + "... (report cut to fit the size limit)";
        }

        var occurredUtc = newestTimestamp > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(newestTimestamp).UtcDateTime
            : DateTime.UtcNow;

        var dto = new ErrorReportDto(
            Channel: Channel,
            Kind: kind,
            Content: report,
            AppVersion: AppInfo.Current.VersionString,
            AppBuild: AppInfo.Current.BuildString,
            TestLabel: GetTestLabel(),
            DeviceManufacturer: DeviceInfo.Current.Manufacturer,
            DeviceModel: DeviceInfo.Current.Model,
            OsVersion: GetOsVersion(),
            NetworkType: GetNetworkType(),
            DeviceDetails: GetDeviceDetails(),
            OccurredAtUtc: occurredUtc);

        Directory.CreateDirectory(PendingDir);
        var file = Path.Combine(PendingDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.json");
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(dto));
        File.Move(temp, file, overwrite: true);

        // Saved: these problems are not reported again.
        if (newestTimestamp > 0) Preferences.Default.Set(LastReportedKey, newestTimestamp);

        // Keep only the newest reports if the phone could not send for a long time.
        foreach (var old in Directory.GetFiles(PendingDir, "*.json").OrderByDescending(f => f).Skip(MaxPendingReports))
        {
            try { File.Delete(old); } catch { }
        }
    }

    /// <summary>Sends saved reports, oldest first. Returns how many the server accepted.</summary>
    private static async Task<int> SendPendingAsync()
    {
        if (!Directory.Exists(PendingDir)) return 0;
        var files = Directory.GetFiles(PendingDir, "*.json").OrderBy(f => f).ToList();
        if (files.Count == 0) return 0;

        var services = IPlatformApplication.Current?.Services;
        var auth = services?.GetService<AuthService>();
        var serverConfig = services?.GetService<ServerConfigService>();
        var http = services?.GetService<HttpClient>();
        var token = auth?.Token;

        // The server only accepts reports from a logged-in user; try again after login.
        if (string.IsNullOrEmpty(token) || serverConfig == null || http == null) return 0;
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return 0;

        var url = $"{serverConfig.CurrentUrl.TrimEnd('/')}/api/diagnostics/error-reports";
        var sent = 0;

        foreach (var file in files)
        {
            ErrorReportDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ErrorReportDto>(await File.ReadAllTextAsync(file));
            }
            catch
            {
                dto = null;
            }

            if (dto == null)
            {
                try { File.Delete(file); } catch { }
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(dto) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    File.Delete(file);
                    sent++;
                }
                else if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.RequestEntityTooLarge)
                {
                    // The server will never accept this one; do not retry it forever.
                    File.Delete(file);
                }
                else
                {
                    // Logged out (401), too many reports (429), server down or older server (404): try later.
                    break;
                }
            }
            catch
            {
                break; // network problem: try later
            }
        }

        return sent;
    }

    private static string? BuildReport(out long newestTimestamp, out string kind)
    {
        newestTimestamp = 0;
        kind = "error";

        var sb = new StringBuilder();
        sb.AppendLine($"DeptChat problem report ({BuildKind})");
        sb.AppendLine($"Created:  {DateTime.Now:yyyy-MM-dd HH:mm:ss} (phone time)");
        sb.AppendLine($"App:      {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})");
        sb.AppendLine($"Phone:    {DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}");
        sb.AppendLine($"Android:  {DeviceInfo.Current.VersionString}");

        var hasProblems = false;
        string? exitKind = null;

#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var context = Android.App.Application.Context;
            var am = context.GetSystemService(Android.Content.Context.ActivityService) as Android.App.ActivityManager;

            // 0 = everything Android still keeps for this app (not just the latest few).
            var exits = am?.GetHistoricalProcessExitReasons(context.PackageName, 0, 0)?.ToList()
                        ?? new List<Android.App.ApplicationExitInfo>();

            var lastReported = Preferences.Default.Get(LastReportedKey, 0L);
            var interesting = exits
                .Where(e => e.Timestamp > lastReported && IsProblem(e))
                .OrderByDescending(e => e.Timestamp)
                .ToList();

            if (interesting.Count > 0)
            {
                hasProblems = true;
                newestTimestamp = interesting.Max(e => e.Timestamp);
                var reasons = interesting.Select(e => (Android.App.ApplicationExitInfoReason)e.Reason).ToList();
                exitKind = reasons.Contains(Android.App.ApplicationExitInfoReason.Anr) ? "freeze"
                    : reasons.Any(IsCrash) ? "crash"
                    : "other";
            }

            var anrs = exits.Count(e => (Android.App.ApplicationExitInfoReason)e.Reason == Android.App.ApplicationExitInfoReason.Anr);
            var crashes = exits.Count(e => IsCrash((Android.App.ApplicationExitInfoReason)e.Reason));
            sb.AppendLine();
            sb.AppendLine($"Android keeps {exits.Count} app exits: {anrs} not responding, {crashes} crashes. New problems in this report: {interesting.Count}.");
            sb.AppendLine();
            sb.AppendLine($"App exits (newest first, up to {MaxExitLines}):");
            foreach (var e in exits.OrderByDescending(e => e.Timestamp).Take(MaxExitLines))
            {
                var where = e.Importance <= VisibleImportance ? "on screen " : "background";
                sb.AppendLine($"  {FormatTime(e.Timestamp)}  {ReasonName((Android.App.ApplicationExitInfoReason)e.Reason),-14} {where}  {e.Description}");
            }

            foreach (var e in interesting.Take(MaxTracesInReport))
            {
                sb.AppendLine();
                sb.AppendLine(new string('=', 70));
                sb.AppendLine($"{ReasonName((Android.App.ApplicationExitInfoReason)e.Reason)} at {FormatTime(e.Timestamp)}");
                sb.AppendLine($"Description: {e.Description}");
                sb.AppendLine($"Memory: pss={e.Pss / 1024} MB, rss={e.Rss / 1024} MB, importance={e.Importance}");
                sb.AppendLine(new string('=', 70));

                try
                {
                    using var stream = e.TraceInputStream;
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        sb.AppendLine(reader.ReadToEnd());
                    }
                    else
                    {
                        sb.AppendLine("(Android kept no stack trace for this exit.)");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"(Could not read stack trace: {ex.Message})");
                }
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("Android 10 or older: freeze history is not available on this phone.");
        }
#endif

        // Taken (read and removed) only now, so errors recorded while the report is built are not lost.
        var errors = DiagLog.TakeAll();
        string? errorKind = null;
        if (errors != null)
        {
            hasProblems = true;
            errorKind = errors.Contains(CrashMarker) ? "crash" : LooksLikeNetwork(errors) ? "network" : "error";
            sb.AppendLine();
            sb.AppendLine(new string('=', 70));
            sb.AppendLine("Errors recorded by the app (oldest first)");
            sb.AppendLine(new string('=', 70));
            sb.AppendLine(errors);
        }

        // A freeze or crash matters most, then an app crash, then network/other errors.
        kind = exitKind == "freeze" || exitKind == "crash" ? exitKind
            : errorKind == "crash" ? "crash"
            : errorKind ?? exitKind ?? "error";

        return hasProblems ? sb.ToString() : null;
    }

    /// <summary>Written by <see cref="RecordCrash"/>; marks a report as a crash.</summary>
    private const string CrashMarker = "CRASH (unhandled exception)";
    private static Exception? _lastCrash;

    /// <summary>Called from the app-wide crash handlers: saves the stack trace for the next report.</summary>
    public static void RecordCrash(Exception ex)
    {
        if (ReferenceEquals(ex, _lastCrash)) return; // the same crash can reach more than one handler
        _lastCrash = ex;
        DiagLog.Error(CrashMarker, ex);
    }

    private static bool LooksLikeNetwork(string errors) =>
        errors.Contains("HttpRequestException") || errors.Contains("SocketException")
        || errors.Contains("TimeoutException") || errors.Contains("TaskCanceledException")
        || errors.Contains("(network: None)") || errors.Contains("(network: Local)")
        || errors.Contains("(network: ConstrainedInternet)");

    private static string? GetTestLabel()
    {
        try
        {
            return typeof(FreezeReport).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "DeptChatTestLabel")?.Value;
        }
        catch { return null; }
    }

    private static string GetOsVersion()
    {
#if ANDROID
        return $"Android {DeviceInfo.Current.VersionString} (API {(int)Android.OS.Build.VERSION.SdkInt})";
#else
        return $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}";
#endif
    }

    private static string GetNetworkType()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            if (access != NetworkAccess.Internet) return access.ToString();
            var profiles = Connectivity.Current.ConnectionProfiles.Select(p => p.ToString()).ToList();
            return profiles.Count > 0 ? string.Join("+", profiles) : "Internet";
        }
        catch { return "unknown"; }
    }

    private static string? GetDeviceDetails()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var am = context.GetSystemService(Android.Content.Context.ActivityService) as Android.App.ActivityManager;
            var memory = new Android.App.ActivityManager.MemoryInfo();
            am?.GetMemoryInfo(memory);
            var abi = Android.OS.Build.SupportedAbis?.FirstOrDefault() ?? "?";
            return $"RAM {memory.TotalMem / (1024 * 1024)} MB (free {memory.AvailMem / (1024 * 1024)} MB), " +
                   $"{(Environment.Is64BitProcess ? "64-bit" : "32-bit")} app, ABI {abi}" +
                   (am?.IsLowRamDevice == true ? ", low-RAM phone" : "");
        }
        catch { return null; }
#else
        return null;
#endif
    }

#if ANDROID
    private static bool IsCrash(Android.App.ApplicationExitInfoReason reason) =>
        reason == Android.App.ApplicationExitInfoReason.Crash ||
        reason == Android.App.ApplicationExitInfoReason.CrashNative;

    // Freezes and crashes always count; "low memory" only when the app was on screen
    // (in the background it is just Android freeing memory, which is normal).
    private static bool IsProblem(Android.App.ApplicationExitInfo e)
    {
        var reason = (Android.App.ApplicationExitInfoReason)e.Reason;
        return reason == Android.App.ApplicationExitInfoReason.Anr
            || IsCrash(reason)
            || (reason == Android.App.ApplicationExitInfoReason.LowMemory && e.Importance <= VisibleImportance);
    }

    private static string ReasonName(Android.App.ApplicationExitInfoReason reason) => reason switch
    {
        Android.App.ApplicationExitInfoReason.Anr => "NOT RESPONDING",
        Android.App.ApplicationExitInfoReason.Crash => "CRASH",
        Android.App.ApplicationExitInfoReason.CrashNative => "NATIVE CRASH",
        Android.App.ApplicationExitInfoReason.LowMemory => "low memory",
        Android.App.ApplicationExitInfoReason.UserRequested => "closed by user",
        Android.App.ApplicationExitInfoReason.ExitSelf => "app exited",
        Android.App.ApplicationExitInfoReason.Signaled => "killed",
        Android.App.ApplicationExitInfoReason.Other => "other",
        _ => $"reason {(int)reason}"
    };

    private static string FormatTime(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
#endif
}
