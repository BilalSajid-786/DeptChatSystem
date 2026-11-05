namespace DeptChat.Mobile.Views;

public partial class ChatRoomPage : ContentPage
{
    private ViewModels.ChatRoomViewModel? _vm;
    private bool _hasInitiallyScrolledToBottom;
#if ANDROID
    private Platforms.Android.KeyboardHeightObserver? _keyboardObserver;
    private MessageRecyclerViewTouchListener? _rvTouchListener;
#endif

    public ChatRoomPage(ViewModels.ChatRoomViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        AttachScrollHandler();
        MessagesCollectionView.HandlerChanged += OnCollectionViewHandlerChanged;
    }

    public ChatRoomPage()
    {
        InitializeComponent();
        BindingContext = _vm = new ViewModels.ChatRoomViewModel();
        AttachScrollHandler();
        MessagesCollectionView.HandlerChanged += OnCollectionViewHandlerChanged;
    }

    private void OnCollectionViewHandlerChanged(object? sender, EventArgs e)
    {
        SetupRecyclerView();
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
            if (_vm.Messages.Count > 0 && !_hasInitiallyScrolledToBottom)
            {
                ScrollToBottom(animate: false);
            }
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        SetupRecyclerView();
    }

    private void SetupRecyclerView()
    {
#if ANDROID
        if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            rv.SetItemViewCacheSize(30);
            rv.SetItemAnimator(null);
            rv.NestedScrollingEnabled = true;

            // Expand Android RecycledViewPool so up to 35 bubble views remain warm in memory.
            // This eliminates view destruction and re-inflation lag during scrolling!
            rv.GetRecycledViewPool()?.SetMaxRecycledViews(0, 35);

            if (_rvTouchListener != null)
            {
                rv.RemoveOnItemTouchListener(_rvTouchListener);
            }

            var ctx = rv.Context ?? Android.App.Application.Context;
            _rvTouchListener = new MessageRecyclerViewTouchListener(
                ctx,
                rv,
                GetMessageAtAdapterPosition,
                TriggerMessageActionMenu);

            rv.AddOnItemTouchListener(_rvTouchListener);
        }
#endif
    }

    private ViewModels.ChatMessageItem? GetMessageAtAdapterPosition(int position)
    {
        if (_vm != null && position >= 0 && position < _vm.Messages.Count)
        {
            return _vm.Messages[position];
        }
        return null;
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
#if ANDROID
                if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv &&
                    rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
                {
                    int targetIndex = _vm?.Messages.IndexOf(item) ?? -1;
                    if (targetIndex >= 0)
                    {
                        lm.ScrollToPositionWithOffset(targetIndex, 0);
                        return;
                    }
                }
#endif
                await Task.Delay(40);
                MessagesCollectionView.ScrollTo(item, position: ScrollToPosition.Start, animate: false);
            }
            catch { }
        });
    }

    private DateTime _lastOlderMessagesFetchTime = DateTime.MinValue;

    private void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (_vm == null) return;

        // Only trigger older messages if:
        // 1. Initial scroll to bottom has completed
        // 2. User is at the top (FirstVisibleItemIndex == 0)
        // 3. User is actively dragging/scrolling upwards (VerticalDelta < 0)
        if (_hasInitiallyScrolledToBottom &&
            e.FirstVisibleItemIndex == 0 &&
            e.VerticalDelta < 0 &&
            _vm.HasMoreOlderMessages &&
            !_vm.IsLoadingOlderMessages &&
            !_vm.IsLoadingMessages)
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
#if ANDROID
        if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rvTouchCheck)
        {
            // If the user is actively touching, dragging, or flinging the list, never interrupt!
            if (rvTouchCheck.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
            {
                return;
            }
        }
#endif

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
#if ANDROID
                    if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rvDelayedCheck)
                    {
                        if (rvDelayedCheck.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                        {
                            return;
                        }
                    }
#endif
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
#if ANDROID
            if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rvCheck)
            {
                if (rvCheck.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                {
                    return;
                }
            }
#endif
            if (_vm?.Messages.Count > 0)
            {
                var lastIndex = _vm.Messages.Count - 1;
                var lastItem = _vm.Messages[lastIndex];

                MessagesCollectionView.ScrollTo(lastItem, position: ScrollToPosition.End, animate: animate);
                _hasInitiallyScrolledToBottom = true;

#if ANDROID
                if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    rv.Post(() =>
                    {
                        try
                        {
                            if (rv.ScrollState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                            {
                                rv.ScrollToPosition(lastIndex);
                            }
                        }
                        catch { }
                    });
                }
#endif
            }
        }
        catch { }
    }

    private void TriggerMessageActionMenu(ViewModels.ChatMessageItem item)
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

        _vm?.ShowMessageMenuCommand.Execute(item);
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
        _hasInitiallyScrolledToBottom = false;

        SetupRecyclerView();

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

#if ANDROID
public class MessageRecyclerViewTouchListener : Java.Lang.Object, AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
{
    private readonly Android.Views.GestureDetector _gestureDetector;
    private readonly AndroidX.RecyclerView.Widget.RecyclerView _recyclerView;
    private readonly Func<int, ViewModels.ChatMessageItem?> _getItemAtPosition;
    private readonly Action<ViewModels.ChatMessageItem> _onLongPress;

    public MessageRecyclerViewTouchListener(
        Android.Content.Context context,
        AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
        Func<int, ViewModels.ChatMessageItem?> getItemAtPosition,
        Action<ViewModels.ChatMessageItem> onLongPress)
    {
        _recyclerView = recyclerView;
        _getItemAtPosition = getItemAtPosition;
        _onLongPress = onLongPress;

        _gestureDetector = new Android.Views.GestureDetector(context, new LongPressGestureListener(this));
        _gestureDetector.IsLongpressEnabled = true;
    }

    public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
    {
        // Deliver event to GestureDetector to track long press holding
        _gestureDetector.OnTouchEvent(e);

        // Always return false: allows RecyclerView to receive the full touch stream
        // and handle smooth scrolling and flinging without any interference or delays!
        return false;
    }

    public void OnTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
    {
        _gestureDetector.OnTouchEvent(e);
    }

    public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
    {
    }

    private void HandleLongPress(Android.Views.MotionEvent e)
    {
        var child = _recyclerView.FindChildViewUnder(e.GetX(), e.GetY());
        if (child == null) return;

        var holder = _recyclerView.FindContainingViewHolder(child);
        if (holder == null) return;

        int pos = holder.BindingAdapterPosition;
        if (pos == AndroidX.RecyclerView.Widget.RecyclerView.NoPosition)
        {
            pos = holder.AbsoluteAdapterPosition;
        }

        if (pos >= 0)
        {
            var item = _getItemAtPosition(pos);
            if (item != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _onLongPress(item);
                });
            }
        }
    }

    private class LongPressGestureListener : Android.Views.GestureDetector.SimpleOnGestureListener
    {
        private readonly MessageRecyclerViewTouchListener _parent;

        public LongPressGestureListener(MessageRecyclerViewTouchListener parent)
        {
            _parent = parent;
        }

        public override void OnLongPress(Android.Views.MotionEvent? e)
        {
            if (e != null)
            {
                _parent.HandleLongPress(e);
            }
        }

        public override bool OnDown(Android.Views.MotionEvent? e)
        {
            return true;
        }
    }
}
#endif
