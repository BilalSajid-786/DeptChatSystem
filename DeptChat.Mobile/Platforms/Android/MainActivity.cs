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
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
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

        // Android 13+ (API 33+) Runtime Notification Permission Request
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 101);
            }
        }

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
