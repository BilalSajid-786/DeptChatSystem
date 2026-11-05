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
[IntentFilter(
    new[] { Android.Content.Intent.ActionSend },
    Categories = new[] { Android.Content.Intent.CategoryDefault },
    DataMimeType = "image/*")]
[IntentFilter(
    new[] { Android.Content.Intent.ActionSend },
    Categories = new[] { Android.Content.Intent.CategoryDefault },
    DataMimeType = "application/pdf")]
[IntentFilter(
    new[] { Android.Content.Intent.ActionSendMultiple },
    Categories = new[] { Android.Content.Intent.CategoryDefault },
    DataMimeType = "image/*")]
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

        // Handle incoming external share (e.g. JazzCash or banking transaction receipt)
        HandleIncomingIntent(Intent);
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleIncomingIntent(intent);
    }

    private void HandleIncomingIntent(Android.Content.Intent? intent)
    {
        if (intent == null) return;

        if (intent.Action == Android.Content.Intent.ActionSend)
        {
            Android.Net.Uri? uri = null;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                uri = intent.GetParcelableExtra(Android.Content.Intent.ExtraStream, Java.Lang.Class.FromType(typeof(Android.Net.Uri))) as Android.Net.Uri;
            }
            else
            {
#pragma warning disable CS0618
                uri = intent.GetParcelableExtra(Android.Content.Intent.ExtraStream) as Android.Net.Uri;
#pragma warning restore CS0618
            }

            if (uri == null && intent.ClipData != null && intent.ClipData.ItemCount > 0)
            {
                uri = intent.ClipData.GetItemAt(0)?.Uri;
            }

            if (uri != null)
            {
                CopyAndStoreSharedUri(uri, intent.Type);
            }
        }
        else if (intent.Action == Android.Content.Intent.ActionSendMultiple)
        {
            if (intent.ClipData != null && intent.ClipData.ItemCount > 0)
            {
                var uri = intent.ClipData.GetItemAt(0)?.Uri;
                if (uri != null)
                {
                    CopyAndStoreSharedUri(uri, intent.Type);
                }
            }
        }
    }

    private void CopyAndStoreSharedUri(Android.Net.Uri uri, string? mimeType)
    {
        try
        {
            using var stream = ContentResolver?.OpenInputStream(uri);
            if (stream == null) return;

            var ext = ".jpg";
            if (mimeType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true) ext = ".png";
            else if (mimeType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true) ext = ".pdf";

            var cacheDir = FileSystem.CacheDirectory;
            var fileName = $"shared_receipt_{Guid.NewGuid():N}{ext}";
            var destPath = Path.Combine(cacheDir, fileName);

            using (var destStream = File.Create(destPath))
            {
                stream.CopyTo(destStream);
            }

            Services.IncomingShareService.Instance.SetPendingShare(destPath, fileName, mimeType);

            // Bring user to department selector screen so they can choose where to send receipt
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.GoToAsync("//DepartmentsPage");
                    }
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] CopyAndStoreSharedUri error: {ex.Message}");
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
