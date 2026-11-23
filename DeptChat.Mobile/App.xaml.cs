using DeptChat.Mobile.Views;
using DeptChat.Mobile.Services;

namespace DeptChat.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow), (handler, view) =>
        {
#if ANDROID
            handler.PlatformView.Window?.SetSoftInputMode(Android.Views.SoftInput.AdjustResize);
#endif
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        window.Created += (s, e) =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                await CheckAndEnforceUpdateAsync();
            });
            // Problem reports are sent automatically (no popup); 4 s so the first screen comes first.
            _ = FreezeReport.CheckAndSendAsync(delayMs: 4000);
        };

        window.Resumed += (s, e) =>
        {
            _ = CheckAndEnforceUpdateAsync();
            _ = FreezeReport.CheckAndSendAsync(delayMs: 3000, throttle: true);
        };

        window.Destroying += (s, e) =>
        {
#if ANDROID
            Platforms.Android.AppPresenceService.NotifyServerOffline();
#endif
        };
        return window;
    }

    protected override void OnStart()
    {
        base.OnStart();
        _ = CheckAndEnforceUpdateAsync();
    }

    /// <summary>False while the app is in the background. Lists use it to skip timed refreshes.</summary>
    public static bool IsInForeground { get; private set; } = true;

    /// <summary>Raised when the app comes back to the foreground, so open lists can catch up on missed messages.</summary>
    public static event Action? AppResumed;

    /// <summary>Raised when the app goes to the background (home, lock, other app), so an open chat can leave its room.</summary>
    public static event Action? AppSleeping;

    protected override void OnSleep()
    {
        base.OnSleep();
        IsInForeground = false;
        AppSleeping?.Invoke();
    }

    protected override void OnResume()
    {
        base.OnResume();
        IsInForeground = true;
        _ = CheckAndEnforceUpdateAsync();
        AppResumed?.Invoke();
    }

    /// <summary>
    /// Checks server API for required app updates.
    /// If outdated, replaces current window root page with the non-dismissible ForceUpdatePage.
    /// </summary>
    public static async Task CheckAndEnforceUpdateAsync(bool silentIfNotRequired = true)
    {
        try
        {
            var versionService = IPlatformApplication.Current?.Services.GetService<AppVersionService>();
            if (versionService == null) return;

            var check = await versionService.CheckVersionAsync();
            if (check.IsUpdateRequired)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (Current?.Windows.Count > 0)
                    {
                        var window = Current.Windows[0];
                        if (window.Page is not ForceUpdatePage)
                        {
                            window.Page = new ForceUpdatePage(check);
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] CheckAndEnforceUpdateAsync error: {ex.Message}");
        }
    }
}
