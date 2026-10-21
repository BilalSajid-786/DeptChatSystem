using System.Net.Http;

namespace DeptChat.Mobile.Services;

public static class FastHttpHandler
{
    /// <summary>
    /// Creates a dedicated HttpMessageHandler for mobile and desktop.
    /// On Android, uses the native platform handler (AndroidMessageHandler) which properly binds
    /// to the active Wi-Fi interface, respects cleartext HTTP permissions for LAN, and manages TLS.
    /// Returns a new instance every time so SignalR disposal never affects HttpClient.
    /// </summary>
    public static HttpMessageHandler CreateHandler()
    {
#if ANDROID
        return new Xamarin.Android.Net.AndroidMessageHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };
#else
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
#endif
    }
}
