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
        return new Window(new AppShell());
    }
}
