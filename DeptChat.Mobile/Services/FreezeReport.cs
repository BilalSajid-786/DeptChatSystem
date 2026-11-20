#if DEPTCHAT_DIAG
using System.Text;

namespace DeptChat.Mobile.Services;

/// <summary>
/// Internal test builds only (build with -p:DeptChatDiag=true).
/// Android 11+ keeps a record of why this app stopped, including the full stack trace of every
/// "isn't responding" freeze (ANR). On start this reads that whole history and, if there are new
/// freezes or crashes (or errors recorded by <see cref="DiagLog"/>), offers to share them as a text
/// file (e.g. over WhatsApp) so testers can send them without a PC or USB cable.
/// Normal background clean-ups by Android ("low memory" / "too many empty" while the app is not
/// on screen) are listed for context but never trigger the prompt. Nothing is sent automatically.
/// </summary>
public static class FreezeReport
{
    // New key: earlier builds stored a timestamp that would hide older freezes still kept by Android.
    private const string LastReportedKey = "diag_last_reported_exit_ts_v2";

    // Android "importance" 100 = on screen, 200 = visible; higher = in the background.
    private const int VisibleImportance = 200;

    private const int MaxTracesInReport = 5;
    private const int MaxExitLines = 40;

    public static async Task CheckAndOfferAsync()
    {
        try
        {
            // Let the first screen appear before showing anything.
            await Task.Delay(4000);

            // Reading Android's exit history and stack traces can take a while: never on the screen thread.
            var (report, newestTimestamp, path) = await Task.Run(() =>
            {
                var text = BuildReport(out var newest);
                if (text == null) return ((string?)null, 0L, (string?)null);

                var file = Path.Combine(FileSystem.CacheDirectory, $"deptchat-freeze-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(file, text);
                return (text, newest, file);
            });
            if (report == null || path == null) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page == null) return;

                var share = await page.DisplayAlert(
                    "Test build: problem report",
                    "The app froze, crashed or hit an error. Share the report with the developer?",
                    "Share report", "Later");

                if (!share) return;

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "DeptChat problem report",
                    File = new ShareFile(path, "text/plain")
                });

                // Only stop offering once it has been shared.
                if (newestTimestamp > 0) Preferences.Default.Set(LastReportedKey, newestTimestamp);
                DiagLog.Clear();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FreezeReport] {ex.Message}");
        }
    }

    private static string? BuildReport(out long newestTimestamp)
    {
        newestTimestamp = 0;
        var errors = DiagLog.ReadAll();

        var sb = new StringBuilder();
        sb.AppendLine("DeptChat problem report (internal test build)");
        sb.AppendLine($"Created:  {DateTime.Now:yyyy-MM-dd HH:mm:ss} (phone time)");
        sb.AppendLine($"App:      {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})");
        sb.AppendLine($"Phone:    {DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}");
        sb.AppendLine($"Android:  {DeviceInfo.Current.VersionString}");

        var hasProblems = false;

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

        if (errors != null)
        {
            hasProblems = true;
            sb.AppendLine();
            sb.AppendLine(new string('=', 70));
            sb.AppendLine("Errors recorded by the app (oldest first)");
            sb.AppendLine(new string('=', 70));
            sb.AppendLine(errors);
        }

        return hasProblems ? sb.ToString() : null;
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
#endif
