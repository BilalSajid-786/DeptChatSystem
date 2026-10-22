#if ANDROID
using Android.App;
using AndroidView = Android.Views.View;
using AndroidRect = Android.Graphics.Rect;
using Android.Views;

namespace DeptChat.Mobile.Platforms.Android;

/// <summary>
/// Universal observer for detecting software keyboard height on Android.
/// Seamlessly supports devices with broken native AdjustResize (e.g. Infinix XOS, Tecno HiOS, Xiaomi MIUI)
/// while coexisting without side-effects on devices where the OS handles resizing (e.g. Samsung One UI).
/// </summary>
public class KeyboardHeightObserver : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
{
    private readonly Activity _activity;
    private readonly Action<double> _onKeyboardHeightChanged;
    private readonly AndroidView? _contentView;
    private int _lastDiff = -1;

    public KeyboardHeightObserver(Activity activity, Action<double> onKeyboardHeightChanged)
    {
        _activity = activity;
        _onKeyboardHeightChanged = onKeyboardHeightChanged;
        _contentView = activity.FindViewById<AndroidView>(global::Android.Resource.Id.Content) ?? activity.Window?.DecorView;
        _contentView?.ViewTreeObserver?.AddOnGlobalLayoutListener(this);
    }

    public void Detach()
    {
        try
        {
            _contentView?.ViewTreeObserver?.RemoveOnGlobalLayoutListener(this);
        }
        catch { }
    }

    public void OnGlobalLayout()
    {
        if (_contentView == null || _activity == null) return;

        var r = new AndroidRect();
        _contentView.GetWindowVisibleDisplayFrame(r);

        var location = new int[2];
        _contentView.GetLocationOnScreen(location);
        int contentBottomOnScreen = location[1] + _contentView.Height;

        // If the window was NOT resized by the OS (e.g. on Infinix/Tecno/Transsion/Xiaomi):
        // contentBottomOnScreen is near the physical bottom of the screen, but r.Bottom is above the keyboard.
        // The difference is the obscured portion (keyboard height).
        // If the window WAS resized by the OS (e.g. on Samsung):
        // _contentView shrank, so contentBottomOnScreen matches r.Bottom, and diff is ~0.
        int diff = contentBottomOnScreen - r.Bottom;

        // Threshold to ignore status bar / navigation bar insets
        int keyboardPx = diff > 100 ? diff : 0;

        if (keyboardPx != _lastDiff)
        {
            _lastDiff = keyboardPx;
            var density = _activity.Resources?.DisplayMetrics?.Density ?? 1.0f;
            double keyboardDp = keyboardPx / density;
            _onKeyboardHeightChanged(keyboardDp);
        }
    }
}
#endif
