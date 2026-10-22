namespace DeptChat.Mobile.Services;

public class ServerConfigService
{
    public const string LocalWifiUrl = "http://192.168.0.108:5051";
    public const string CloudflareUrl = "https://practitioners-ribbon-prevent-manufacturing.trycloudflare.com";
    public const string DefaultUrl = CloudflareUrl;
    private const string PrefKey = "DeptChat_ServerUrl";

    public string CurrentUrl
    {
        get
        {
            var saved = Preferences.Default.Get(PrefKey, DefaultUrl);
            if (string.IsNullOrWhiteSpace(saved) || 
                (saved.Contains("trycloudflare.com") && !saved.Contains("practitioners-ribbon-prevent-manufacturing")) ||
                saved.Contains("warcraft-offline-serving-athens") ||
                saved.Contains("turning-norfolk-operates-tradition") ||
                saved.Contains("unto-intensive-lips-compare"))
            {
                Preferences.Default.Set(PrefKey, CloudflareUrl);
                return CloudflareUrl;
            }
            return NormalizeUrl(saved);
        }
        set
        {
            var cleaned = NormalizeUrl(string.IsNullOrWhiteSpace(value) ? DefaultUrl : value);
            Preferences.Default.Set(PrefKey, cleaned);
            OnUrlChanged?.Invoke(cleaned);
        }
    }

    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return DefaultUrl;
        var trimmed = url.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }
        return trimmed;
    }

    public event Action<string>? OnUrlChanged;
}
