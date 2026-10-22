using Android.App;
using Android.Content;
using Android.OS;
using System.Text.Json;
using DeptChat.Shared.DTOs;
using DeptChat.Mobile.Services;

namespace DeptChat.Mobile.Platforms.Android;

[Service(Exported = false)]
public class AppPresenceService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        return StartCommandResult.Sticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        base.OnTaskRemoved(rootIntent);

        // Immediate notification when user clears the app from Recents / background
        NotifyServerOffline();

        StopSelf();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        NotifyServerOffline();
    }

    public static void NotifyServerOffline()
    {
        try
        {
            var authJson = Preferences.Default.Get("auth_current_user", string.Empty);
            if (string.IsNullOrWhiteSpace(authJson)) return;

            var user = JsonSerializer.Deserialize<AuthResponseDto>(authJson);
            if (user == null || string.IsNullOrWhiteSpace(user.Token)) return;

            var serverUrl = Preferences.Default.Get("DeptChat_ServerUrl", ServerConfigService.CloudflareUrl);
            var url = $"{serverUrl.TrimEnd('/')}/api/presence/offline";

            using var handler = FastHttpHandler.CreateHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);

            var task = client.PostAsync(url, null);
            task.Wait(1200);
        }
        catch
        {
            // Best effort before process termination
        }
    }
}
