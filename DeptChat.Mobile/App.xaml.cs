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
        };

        window.Resumed += (s, e) =>
        {
            _ = CheckAndEnforceUpdateAsync();
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

    protected override void OnResume()
    {
        base.OnResume();
        _ = CheckAndEnforceUpdateAsync();
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
