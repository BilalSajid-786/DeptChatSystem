namespace DeptChat.Mobile.Services;

public class ServerConfigService
{
    public const string LocalWifiUrl = "http://192.168.0.109:5051";
    public const string ProductionUrl = "https://179.197.255.148.sslip.io";
    public const string DirectIpUrl = "http://179.197.255.148:5051";
    public const string CloudflareUrl = ProductionUrl;
    public const string DefaultUrl = ProductionUrl;
    private const string PrefKey = "DeptChat_ServerUrl";

    // Test builds only: -p:DeptChatServerUrl=http://192.168.x.x:5051 makes this build always use that
    // server (a saved address from an earlier install is ignored). Not set for customer builds.
    private static readonly string? BuildServerUrl = ReadBuildServerUrl();

    private static string? ReadBuildServerUrl()
    {
        try
        {
            var value = typeof(ServerConfigService).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "DeptChatServerUrl")?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : NormalizeUrl(value);
        }
        catch { return null; }
    }

    public string CurrentUrl
    {
        get
        {
            if (BuildServerUrl != null) return BuildServerUrl;

            var saved = Preferences.Default.Get(PrefKey, DefaultUrl);
            if (string.IsNullOrWhiteSpace(saved) || 
                saved.Contains("trycloudflare.com") ||
                saved.Contains("happened-minute-agricultural-heath") ||
                saved.Contains("cosmetics-environmental-respected-periods") ||
                saved.Contains("drinks-mass-noon-preview") ||
                saved.Contains("subscription-left-legs-kingston") ||
                saved.Contains("lived-varying-consent-atom") ||
                saved.Contains("practitioners-ribbon-prevent-manufacturing") ||
                saved.Contains("warcraft-offline-serving-athens") ||
                saved.Contains("turning-norfolk-operates-tradition") ||
                saved.Contains("unto-intensive-lips-compare"))
            {
                Preferences.Default.Set(PrefKey, DefaultUrl);
                return DefaultUrl;
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
            trimmed = "https://" + trimmed;
        }
        return trimmed;
    }

    public event Action<string>? OnUrlChanged;
}
