using System.Diagnostics;

namespace DeptChat.Mobile.Services;

/// <summary>
/// Internal test builds only (build with -p:DeptChatDiag=true). Records errors that the app otherwise
/// swallows (e.g. why the department list failed to load) so they can be shared in the freeze report.
/// In normal builds every call to <see cref="Error"/> is removed by the compiler and nothing is written.
/// </summary>
public static class DiagLog
{
    [Conditional("DEPTCHAT_DIAG")]
    public static void Error(string where, Exception ex)
    {
#if DEPTCHAT_DIAG
        try
        {
            var details = ex.ToString();
            if (details.Length > 1500) details = details[..1500] + " ...";

            var entry =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {where}  (network: {Connectivity.Current.NetworkAccess}){Environment.NewLine}" +
                $"    {details.Replace(Environment.NewLine, Environment.NewLine + "    ")}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(FilePath, entry);

                // Keep the file small: drop the oldest half once it grows past ~200 KB.
                var info = new FileInfo(FilePath);
                if (info.Length > 200_000)
                {
                    var text = File.ReadAllText(FilePath);
                    File.WriteAllText(FilePath, text[(text.Length / 2)..]);
                }
            }
        }
        catch { }
#endif
    }

#if DEPTCHAT_DIAG
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "diag_errors.log");

    public static string? ReadAll()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return null;
                var text = File.ReadAllText(FilePath);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch { return null; }
    }

    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
        }
        catch { }
    }
#endif
}
