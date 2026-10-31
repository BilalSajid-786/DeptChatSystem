namespace DeptChat.Mobile.Services;

public class ServerConfigService
{
    public const string LocalWifiUrl = "http://192.168.0.109:5051";
    public const string CloudflareUrl = "https://happened-minute-agricultural-heath.trycloudflare.com";
    public const string DefaultUrl = CloudflareUrl;
    private const string PrefKey = "DeptChat_ServerUrl";

    public string CurrentUrl
    {
        get
        {
            var saved = Preferences.Default.Get(PrefKey, DefaultUrl);
            if (string.IsNullOrWhiteSpace(saved) || 
                (saved.Contains("trycloudflare.com") && !saved.Contains("happened-minute-agricultural-heath")) ||
                saved.Contains("cosmetics-environmental-respected-periods") ||
                saved.Contains("drinks-mass-noon-preview") ||
                saved.Contains("subscription-left-legs-kingston") ||
                saved.Contains("lived-varying-consent-atom") ||
                saved.Contains("practitioners-ribbon-prevent-manufacturing") ||
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
