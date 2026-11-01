using System.Windows.Input;

namespace DeptChat.Mobile.Behaviors;

public class LongPressBehavior : Behavior<View>
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(LongPressBehavior), null);

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(LongPressBehavior), null);

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private View? _attachedView;

#if ANDROID
    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _attachedView = bindable;
        bindable.HandlerChanged += OnHandlerChanged;
        if (bindable.Handler?.PlatformView is Android.Views.View nativeView)
        {
            SetupNativeLongClick(nativeView);
        }
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        bindable.HandlerChanged -= OnHandlerChanged;
        if (bindable.Handler?.PlatformView is Android.Views.View nativeView)
        {
            nativeView.LongClick -= OnNativeLongClick;
        }
        _attachedView = null;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View view && view.Handler?.PlatformView is Android.Views.View nativeView)
        {
            SetupNativeLongClick(nativeView);
        }
    }

    private void SetupNativeLongClick(Android.Views.View nativeView)
    {
        nativeView.LongClickable = true;
        nativeView.LongClick -= OnNativeLongClick;
        nativeView.LongClick += OnNativeLongClick;
    }

    private void OnNativeLongClick(object? sender, Android.Views.View.LongClickEventArgs e)
    {
        e.Handled = true;
        TriggerAction();
    }
#else
    private CancellationTokenSource? _cts;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _attachedView = bindable;
        var pgr = new PointerGestureRecognizer();
        pgr.PointerPressed += OnPointerPressed;
        pgr.PointerReleased += OnPointerReleased;
        pgr.PointerExited += OnPointerReleased;
        bindable.GestureRecognizers.Add(pgr);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        _cts?.Cancel();
        _attachedView = null;
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(450, token);
                if (!token.IsCancellationRequested)
                {
                    MainThread.BeginInvokeOnMainThread(() => TriggerAction());
                }
            }
            catch (TaskCanceledException) { }
        });
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        _cts?.Cancel();
    }
#endif

    private void TriggerAction()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch { }

        try
        {
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(50));
        }
        catch { }

        if (Command != null && Command.CanExecute(CommandParameter))
        {
            Command.Execute(CommandParameter);
        }
    }
}
