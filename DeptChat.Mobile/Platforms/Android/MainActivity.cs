using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using Firebase.Messaging;

namespace DeptChat.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme", 
    MainLauncher = true, 
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Window != null)
        {
            WindowCompat.SetDecorFitsSystemWindows(Window, true);
            Window.SetSoftInputMode(SoftInput.AdjustResize);
        }

        // Initialize Android notification channel on startup
        CreateNotificationChannel();

        // Android 13+ (API 33+) Runtime Notification Permission Request
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 101);
            }
        }

        // Start presence monitoring service for instant onTaskRemoved offline broadcast
        try
        {
            var serviceIntent = new Android.Content.Intent(this, typeof(Platforms.Android.AppPresenceService));
            StartService(serviceIntent);
        }
        catch { }

        // Fetch and cache the initial FCM device registration token
        try
        {
            FirebaseMessaging.Instance.GetToken().AddOnCompleteListener(new TokenCompleteListener());
        }
        catch
        {
            // Ignore if Google Play Services is pending initialization
        }
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            if (notificationManager != null)
            {
                var channel = new NotificationChannel(
                    Platforms.Android.MyFirebaseMessagingService.ChannelId,
                    "Department Chat Notifications",
                    NotificationImportance.High)
                {
                    Description = "Incoming messages and alerts from department chat",
                    LockscreenVisibility = NotificationVisibility.Public
                };
                channel.EnableVibration(true);
                channel.SetShowBadge(true);
                notificationManager.CreateNotificationChannel(channel);
            }
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Platforms.Android.AppPresenceService.NotifyServerOffline();
    }

    private class TokenCompleteListener : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful && task.Result != null)
            {
                var token = task.Result.ToString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    Preferences.Default.Set("fcm_device_token", token);
                }
            }
        }
    }
}
