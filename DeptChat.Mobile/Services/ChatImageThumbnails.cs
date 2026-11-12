using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DeptChat.Mobile.Services;

/// <summary>
/// Makes small copies of chat pictures for the message list.
/// Pictures are often full phone screenshots (e.g. 1080×2400, ~10 MB once decoded) shown in a
/// 240×200 box; drawing them at full size caused hitches while scrolling. Each picture is
/// downloaded once (same public URL as before), shrunk to fit ~640 px, saved in the app's cache
/// folder and reused. Tapping a picture still opens the original full-size image.
/// If anything fails, the caller falls back to the original URL (the previous behaviour).
/// </summary>
public static class ChatImageThumbnails
{
    private const int MaxSidePx = 640;
    private static readonly SemaphoreSlim Throttle = new(2);
    private static readonly ConcurrentDictionary<string, Task<string?>> Pending = new();
    private static readonly ConcurrentDictionary<string, string> Known = new();
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient(FastHttpHandler.CreateHandler())
    {
        Timeout = TimeSpan.FromSeconds(30)
    });

    private static string CacheDir => Path.Combine(FileSystem.CacheDirectory, "chat_thumbs");

    public static bool IsRemote(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Path of an already-made small copy, or null. Cheap enough to call while binding rows.</summary>
    public static string? TryGetCached(string url)
    {
        if (Known.TryGetValue(url, out var known)) return known;

        var path = PathFor(url);
        if (File.Exists(path))
        {
            Known[url] = path;
            return path;
        }
        return null;
    }

    /// <summary>Makes (or reuses) the small copy off the UI thread. Returns null on failure.</summary>
    public static Task<string?> GetOrCreateAsync(string url)
    {
        var cached = TryGetCached(url);
        if (cached != null) return Task.FromResult<string?>(cached);

        return Pending.GetOrAdd(url, u => Task.Run(() => CreateAsync(u)));
    }

    /// <summary>Deletes all small copies (called on logout).</summary>
    public static void Clear()
    {
        Known.Clear();
        Pending.Clear();
        _ = Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, recursive: true);
            }
            catch { }
        });
    }

    private static string PathFor(string url)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheDir, hash + ".img");
    }

    private static async Task<string?> CreateAsync(string url)
    {
        await Throttle.WaitAsync().ConfigureAwait(false);
        try
        {
#if ANDROID
            var bytes = await Http.Value.GetByteArrayAsync(url).ConfigureAwait(false);
            Directory.CreateDirectory(CacheDir);
            var path = PathFor(url);
            var tmp = path + ".tmp";

            if (!Platforms.Android.ThumbnailMaker.TryWrite(bytes, MaxSidePx, tmp)) return null;

            File.Move(tmp, path, overwrite: true);
            Known[url] = path;
            return path;
#else
            await Task.CompletedTask;
            return null;
#endif
        }
        catch
        {
            return null;
        }
        finally
        {
            Throttle.Release();
            Pending.TryRemove(url, out _);
        }
    }
}
