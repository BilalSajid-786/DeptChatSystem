namespace DeptChat.Mobile.Services;

/// <summary>
/// Records errors that the app otherwise swallows (e.g. why the department list failed to load) and
/// crashes, so they go into the next automatic problem report (<see cref="FreezeReport"/>).
/// Active in every build: test builds report to the "internal" folder, customer builds to "live".
/// </summary>
public static class DiagLog
{
    public static void Error(string where, Exception ex)
    {
        try
        {
            var details = ex.ToString();
            if (details.Length > 4000) details = details[..4000] + " ...";

            string network;
            try { network = Connectivity.Current.NetworkAccess.ToString(); } catch { network = "unknown"; }

            var entry =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {where}  (network: {network}){Environment.NewLine}" +
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
    }

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

    /// <summary>Reads and removes everything recorded so far in one step (nothing recorded in between is lost).</summary>
    public static string? TakeAll()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return null;
                var text = File.ReadAllText(FilePath);
                File.Delete(FilePath);
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
}
