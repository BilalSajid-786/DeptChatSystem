namespace DeptChat.Mobile.Views;

public partial class ChatRoomPage : ContentPage
{
    private ViewModels.ChatRoomViewModel? _vm;
#if ANDROID
    private Platforms.Android.KeyboardHeightObserver? _keyboardObserver;
#endif

    public ChatRoomPage(ViewModels.ChatRoomViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        AttachScrollHandler();
    }

    public ChatRoomPage()
    {
        InitializeComponent();
        BindingContext = _vm = new ViewModels.ChatRoomViewModel();
        AttachScrollHandler();
    }

    private void AttachScrollHandler()
    {
        if (_vm == null) return;
        _vm.RequestScrollToBottom -= OnRequestScrollToBottom;
        _vm.RequestScrollToBottom += OnRequestScrollToBottom;
        _vm.RequestScrollToItem -= OnRequestScrollToItem;
        _vm.RequestScrollToItem += OnRequestScrollToItem;

        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        _vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        _vm.Messages.CollectionChanged += OnMessagesCollectionChanged;
    }

    private void DetachScrollHandler()
    {
        if (_vm == null) return;
        _vm.RequestScrollToBottom -= OnRequestScrollToBottom;
        _vm.RequestScrollToItem -= OnRequestScrollToItem;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.ChatRoomViewModel.IsLoadingMessages) && _vm?.IsLoadingMessages == false)
        {
            ScrollToBottom(animate: false);
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            rv.SetItemViewCacheSize(20);
            rv.SetItemAnimator(null);
            rv.HasFixedSize = false;
        }
#endif
    }

    private void OnMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_vm?.IsLoadingOlderMessages == true || _vm?.IsLoadingMessages == true) return;

        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom(animate: false);
        }
    }

    private void OnRequestScrollToItem(ViewModels.ChatMessageItem item)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Delay(40);
                MessagesCollectionView.ScrollTo(item, position: ScrollToPosition.Start, animate: false);
            }
            catch { }
        });
    }

    private DateTime _lastOlderMessagesFetchTime = DateTime.MinValue;

    private void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _longPressCts?.Cancel();

        if (_vm == null) return;

        if (e.FirstVisibleItemIndex == 0 && _vm.HasMoreOlderMessages && !_vm.IsLoadingOlderMessages && !_vm.IsLoadingMessages)
        {
            if ((DateTime.UtcNow - _lastOlderMessagesFetchTime).TotalMilliseconds > 2000)
            {
                _lastOlderMessagesFetchTime = DateTime.UtcNow;
                _vm.LoadOlderMessagesCommand.Execute(null);
            }
        }
    }

    private void OnRequestScrollToBottom()
    {
        ScrollToBottom(animate: false);
    }

    private CancellationTokenSource? _scrollToBottomCts;

    public void ScrollToBottom(bool animate = false)
    {
        _scrollToBottomCts?.Cancel();
        _scrollToBottomCts?.Dispose();
        _scrollToBottomCts = new CancellationTokenSource();
        var token = _scrollToBottomCts.Token;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                PerformScroll(animate);
                await Task.Delay(100, token);
                if (!token.IsCancellationRequested)
                {
                    PerformScroll(false);
                }
            }
            catch (TaskCanceledException) { }
        });
    }

    private void PerformScroll(bool animate)
    {
        try
        {
            if (_vm?.Messages.Count > 0)
            {
                var lastIndex = _vm.Messages.Count - 1;
                var lastItem = _vm.Messages[lastIndex];

                MessagesCollectionView.ScrollTo(lastItem, position: ScrollToPosition.End, animate: animate);

#if ANDROID
                if (MessagesCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    rv.Post(() =>
                    {
                        try
                        {
                            rv.ScrollToPosition(lastIndex);
                        }
                        catch { }
                    });
                }
#endif
            }
        }
        catch { }
    }

    private CancellationTokenSource? _longPressCts;
    private Point _pointerPressStartPoint;
    private const double TouchSlop = 12.0;

    private void OnMessagePointerPressed(object? sender, PointerEventArgs e)
    {
        _longPressCts?.Cancel();
        _longPressCts?.Dispose();
        _longPressCts = new CancellationTokenSource();
        var token = _longPressCts.Token;

        var pos = e.GetPosition(sender as View);
        _pointerPressStartPoint = pos.HasValue ? pos.Value : Point.Zero;

        if (sender is Border border && border.BindingContext is ViewModels.ChatMessageItem item)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(400, token);
                    if (!token.IsCancellationRequested && _vm != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
                            }
                            catch { }
                            _vm.ShowMessageMenuCommand.Execute(item);
                        });
                    }
                }
                catch (TaskCanceledException) { }
            }, token);
        }
    }

    private void OnMessagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_longPressCts != null && !_longPressCts.IsCancellationRequested)
        {
            var pos = e.GetPosition(sender as View);
            if (pos.HasValue)
            {
                var dx = Math.Abs(pos.Value.X - _pointerPressStartPoint.X);
                var dy = Math.Abs(pos.Value.Y - _pointerPressStartPoint.Y);
                if (dx > TouchSlop || dy > TouchSlop)
                {
                    _longPressCts.Cancel();
                }
            }
        }
    }

    private void OnMessagePointerReleased(object? sender, PointerEventArgs e)
    {
        _longPressCts?.Cancel();
    }

    private void OnMessagePointerExited(object? sender, PointerEventArgs e)
    {
        _longPressCts?.Cancel();
    }

    private void OnPreviewSliderDragStarted(object? sender, EventArgs e)
    {
        if (BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            vm.OnPreviewSliderDragStarted();
        }
    }

    private void OnPreviewSliderDragCompleted(object? sender, EventArgs e)
    {
        if (sender is Slider slider && BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            _ = vm.SeekVoicePreviewAsync(slider.Value);
        }
    }

    private void OnMessageSliderDragStarted(object? sender, EventArgs e)
    {
        if (sender is Slider slider && slider.BindingContext is ViewModels.ChatMessageItem item && BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            vm.OnMessageSliderDragStarted(item);
        }
    }

    private void OnMessageSliderDragCompleted(object? sender, EventArgs e)
    {
        if (sender is Slider slider && slider.BindingContext is ViewModels.ChatMessageItem item && BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            _ = vm.SeekMessageAudioAsync(item, slider.Value);
        }
    }

    private void OnMessageEntryFocused(object? sender, FocusEventArgs e)
    {
        if (e.IsFocused)
        {
            ScrollToBottom(animate: false);
        }
    }

    private void OnKeyboardHeightChanged(double keyboardDp)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                ChatContentGrid.Margin = new Thickness(0, 0, 0, keyboardDp);

                if (keyboardDp > 0)
                {
                    ScrollToBottom(animate: false);
                }
            }
            catch { }
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity != null)
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity.Window != null)
            {
                AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(activity.Window, true);
                activity.Window.SetSoftInputMode(Android.Views.SoftInput.AdjustResize);
            }

            _keyboardObserver?.Detach();
            _keyboardObserver = new Platforms.Android.KeyboardHeightObserver(activity, OnKeyboardHeightChanged);
        }
#endif

        if (BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            _vm = vm;
            AttachScrollHandler();
            await vm.InitializeAsync();

            // Auto-scroll to the very last message when opening a chat
            ScrollToBottom(animate: false);
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

#if ANDROID
        _keyboardObserver?.Detach();
        _keyboardObserver = null;
#endif

        if (BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            DetachScrollHandler();
            await vm.OnDisappearingAsync();
        }
    }
}
