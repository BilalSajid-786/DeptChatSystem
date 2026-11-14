namespace DeptChat.Mobile.Views;

public partial class ChatRoomPage : ContentPage
{
    private ViewModels.ChatRoomViewModel? _vm;
    private bool _hasInitiallyScrolledToBottom;
    private bool _isUserNearBottom = true;
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

        _vm.PreparingOlderMessagesInsert -= OnPreparingOlderMessagesInsert;
        _vm.PreparingOlderMessagesInsert += OnPreparingOlderMessagesInsert;
        _vm.OlderMessagesInserted -= OnOlderMessagesInserted;
        _vm.OlderMessagesInserted += OnOlderMessagesInserted;

        _vm.BulkLoadFinished -= OnBulkLoadFinished;
        _vm.BulkLoadFinished += OnBulkLoadFinished;
    }

    private void DetachScrollHandler()
    {
        if (_vm == null) return;
        _vm.RequestScrollToBottom -= OnRequestScrollToBottom;
        _vm.RequestScrollToItem -= OnRequestScrollToItem;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        _vm.PreparingOlderMessagesInsert -= OnPreparingOlderMessagesInsert;
        _vm.OlderMessagesInserted -= OnOlderMessagesInserted;
        _vm.BulkLoadFinished -= OnBulkLoadFinished;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The forward popup is built the first time it is opened, not on every chat open.
        if (e.PropertyName == nameof(ViewModels.ChatRoomViewModel.IsForwardModalVisible)
            && _vm?.IsForwardModalVisible == true
            && ForwardModalHost.Content == null)
        {
            ForwardModalHost.Content = new ForwardModalView();
        }

        if (e.PropertyName == nameof(ViewModels.ChatRoomViewModel.IsLoadingMessages) && _vm?.IsLoadingMessages == false)
        {
            if (_vm.Messages.Count > 0 && !_hasInitiallyScrolledToBottom)
            {
                ScrollToBottom(animate: false);
            }
        }
    }

    private int _anchorPosition = -1;
    private int _anchorTopOffset = 0;

    private void OnPreparingOlderMessagesInsert()
    {
#if ANDROID
        if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv &&
            rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
        {
            _anchorPosition = lm.FindFirstVisibleItemPosition();
            var view = lm.FindViewByPosition(_anchorPosition);
            _anchorTopOffset = view?.Top ?? 0;

            // SuppressLayout(true) also cancels any scroll in progress, so only use it when the
            // list is at rest. While the user is scrolling, RecyclerView batches the inserts into
            // its next layout pass on its own.
            _layoutSuppressedForInsert = rv.ScrollState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle;
            if (_layoutSuppressedForInsert)
            {
                try
                {
                    rv.SuppressLayout(true);
                }
                catch { }
            }
        }
#endif
    }

    private void OnOlderMessagesInserted(int insertedCount)
    {
#if ANDROID
        if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv &&
            rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
        {
            if (_layoutSuppressedForInsert)
            {
                try
                {
                    rv.SuppressLayout(false);
                }
                catch { }
                _layoutSuppressedForInsert = false;
            }

            if (insertedCount > 0 && _anchorPosition >= 0)
            {
                // Older messages now usually arrive while the user is still scrolling up
                // (they load ~10 rows before the top). Forcing a position during a fling would
                // stop the scroll, so only restore it when the list is at rest; while scrolling,
                // RecyclerView keeps the visible rows in place itself.
                if (rv.ScrollState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                {
                    int restoredPosition = _anchorPosition + insertedCount;
                    lm.ScrollToPositionWithOffset(restoredPosition, _anchorTopOffset);
                }
                _anchorPosition = -1;
            }
        }
#endif
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
            rv.VerticalScrollBarEnabled = false;
            rv.HorizontalScrollBarEnabled = false;
            rv.HasFixedSize = false;
            rv.NestedScrollingEnabled = false;
            // Smaller caches now that each message type has its own (lighter) row layout:
            // fewer hidden rows and pictures kept in memory means less memory cleanup while scrolling.
            rv.SetItemViewCacheSize(10);
            rv.SetItemAnimator(null);

            // Expand Android RecycledViewPool so cells are recycled instead of re-inflated
            var pool = rv.GetRecycledViewPool();
            if (pool != null)
            {
                for (int vt = 0; vt <= 15; vt++)
                {
                    pool.SetMaxRecycledViews(vt, 15);
                }
            }

            if (rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
            {
                lm.StackFromEnd = true;
                lm.ItemPrefetchEnabled = true;
                lm.InitialPrefetchItemCount = 4;
            }

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

    // Set when messages were added during a bulk load; one scroll happens when it finishes.
    private bool _bulkAddPending;

    private void OnMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_vm?.IsLoadingOlderMessages == true || _vm?.IsLoadingMessages == true) return;

        // During a bulk load (opening a chat, syncing) messages arrive many at a time; scrolling for
        // each one caused dozens of layout passes. Remember it and scroll once at the end instead.
        if (_vm?.IsBulkLoading == true)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) _bulkAddPending = true;
            return;
        }


        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
        {
            if (_isUserNearBottom)
            {
#if ANDROID
                if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rvCheck &&
                    rvCheck.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                {
                    return;
                }
#endif
                ScrollToBottom(animate: false);
            }
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

    // How many rows from the top the next page of older messages starts loading.
    private const int OlderMessagesPrefetchRows = 10;

    private bool _layoutSuppressedForInsert;

    private void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (_vm == null || _vm.Messages.Count == 0) return;

        // Track whether user is looking at bottom or reading older messages
        bool nearBottom = e.LastVisibleItemIndex >= _vm.Messages.Count - 2;
        _isUserNearBottom = nearBottom;

        if (nearBottom)
        {
            _hasInitiallyScrolledToBottom = true;
        }

        // Cancel any pending programmatic scroll to bottom ONLY if user has already arrived at bottom and is scrolling up manually
        if (_hasInitiallyScrolledToBottom && e.VerticalDelta < -1)
        {
            _scrollToBottomCts?.Cancel();
        }

        // Only trigger older messages if:
        // 1. Initial scroll to bottom has completed AND user is NOT looking at bottom
        // 2. We have at least 20 messages loaded
        // 3. User is scrolling up and about 10 rows from the top (FirstVisibleItemIndex <= 10, VerticalDelta < 0).
        //    Starting this early means the next page is usually loaded before the user reaches the top.
        // 4. Server confirmed there are more older messages
        if (_hasInitiallyScrolledToBottom &&
            !_isUserNearBottom &&
            _vm.Messages.Count >= 20 &&
            e.FirstVisibleItemIndex <= OlderMessagesPrefetchRows &&
            e.VerticalDelta < 0 &&
            _vm.HasMoreOlderMessages &&
            !_vm.IsLoadingOlderMessages &&
            !_vm.IsLoadingMessages)
        {
            if ((DateTime.UtcNow - _lastOlderMessagesFetchTime).TotalMilliseconds > 1500)
            {
                _lastOlderMessagesFetchTime = DateTime.UtcNow;
                _vm.LoadOlderMessagesCommand.Execute(null);
            }
        }
    }

    private void OnBulkLoadFinished()
    {
        if (!_bulkAddPending) return;
        _bulkAddPending = false;

        // Same rule as for a single new message: only follow to the bottom if the user is there.
        if (_isUserNearBottom)
        {
#if ANDROID
            if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rvCheck &&
                rvCheck.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
            {
                return;
            }
#endif
            ScrollToBottom(animate: false);
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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PerformScroll(animate);
        });
    }

    private void PerformScroll(bool animate)
    {
        try
        {
            if (_vm?.Messages.Count > 0)
            {
                int lastIndex = _vm.Messages.Count - 1;
                var lastItem = _vm.Messages[lastIndex];

#if ANDROID
                if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    if (rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
                    {
                        lm.StackFromEnd = true;
                    }

                    if (rv.IsComputingLayout)
                    {
                        rv.Post(() => PerformScroll(animate));
                        return;
                    }

                    rv.ScrollToPosition(lastIndex);
                    MessagesCollectionView.ScrollTo(lastIndex, position: ScrollToPosition.End, animate: animate);

                    rv.Post(() =>
                    {
                        try
                        {
                            int currentLast = (_vm?.Messages.Count ?? 0) - 1;
                            if (currentLast >= 0)
                            {
                                rv.ScrollToPosition(currentLast);
                                MessagesCollectionView?.ScrollTo(currentLast, position: ScrollToPosition.End, animate: false);
                            }
                            _hasInitiallyScrolledToBottom = true;
                            _isUserNearBottom = true;
                        }
                        catch { }
                    });
                    return;
                }
#endif
                MessagesCollectionView.ScrollTo(lastItem, position: ScrollToPosition.End, animate: animate);
                _hasInitiallyScrolledToBottom = true;
                _isUserNearBottom = true;
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if DEPTCHAT_TIMING
        StartChatTiming();
#endif
        _hasInitiallyScrolledToBottom = false;
        _isUserNearBottom = true;

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

            // Show the chat screen first and load/draw the messages right after the slide-in
            // (OnNavigatedTo), so building message rows no longer delays the screen opening.
            if (vm.Messages.Count == 0)
            {
                vm.IsLoadingMessages = true; // "Loading messages..." until the first messages are shown
            }
            _initializePending = true;

            // Safety net in case OnNavigatedTo does not arrive (e.g. returning from a popup page).
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), RunPendingInitialize);
        }
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        RunPendingInitialize();
    }

    private bool _initializePending;

    private async void RunPendingInitialize()
    {
        if (!_initializePending || _vm == null) return;
        _initializePending = false;

        var vm = _vm;
        await vm.InitializeAsync();

        // Auto-scroll to the very last message when opening a chat
        ScrollToBottom(animate: false);

#if ANDROID
        if (MessagesCollectionView?.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rvInitial)
        {
            rvInitial.Post(() => ScrollToBottom(animate: false));
            rvInitial.PostDelayed(() => ScrollToBottom(animate: false), 80);
            rvInitial.PostDelayed(() => ScrollToBottom(animate: false), 200);
            rvInitial.PostDelayed(() => ScrollToBottom(animate: false), 400);
        }
#endif
    }

    protected override async void OnDisappearing()
    {
        _initializePending = false;
#if DEPTCHAT_TIMING
        StopChatTiming();
#endif
        base.OnDisappearing();
        _hasInitiallyScrolledToBottom = false;
        _isUserNearBottom = true;

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
#if DEPTCHAT_TIMING
    // ---------------------------------------------------------------------------------
    // Internal chat timing box. Compiled only with -p:DeptChatTiming=true.
    // Lists each freeze over 100 ms with GC runs (gen0/gen1/gen2) and the kinds of rows
    // shown during it (t=text, i=image, d=document, v=voice). Tap the box to copy it.
    // ---------------------------------------------------------------------------------
    private Label? _chatTimingLabel;
    private IDispatcherTimer? _chatTimingTimer;

    private void StartChatTiming()
    {
        Services.PerfTrace.MarkTap("Chat opened");
        Services.PerfTrace.StartFrames();

        if (_chatTimingLabel == null && Content is Grid root)
        {
            _chatTimingLabel = new Label
            {
                FontFamily = "monospace",
                FontSize = 9,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#CC000000"),
                Padding = new Thickness(6, 4),
                Margin = new Thickness(6, 4),
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Start,
                MaximumWidthRequest = 340,
                ZIndex = 1000
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                try
                {
                    await Clipboard.Default.SetTextAsync(Services.PerfTrace.Snapshot());
                    _chatTimingLabel.Text = "Copied to clipboard ✓" + Environment.NewLine + Services.PerfTrace.Snapshot();
                }
                catch { }
            };
            _chatTimingLabel.GestureRecognizers.Add(tap);
            Grid.SetRow(_chatTimingLabel, Math.Min(1, Math.Max(0, root.RowDefinitions.Count - 1)));
            root.Children.Add(_chatTimingLabel);
        }

        _chatTimingTimer ??= Dispatcher.CreateTimer();
        _chatTimingTimer.Interval = TimeSpan.FromSeconds(1);
        _chatTimingTimer.Tick -= OnChatTimingTick;
        _chatTimingTimer.Tick += OnChatTimingTick;
        _chatTimingTimer.Start();
    }

    private void StopChatTiming()
    {
        _chatTimingTimer?.Stop();
        Services.PerfTrace.StopFrames();
    }

    private void OnChatTimingTick(object? sender, EventArgs e)
    {
        if (_chatTimingLabel != null) _chatTimingLabel.Text = Services.PerfTrace.Snapshot();
    }
#endif
}

#if ANDROID
public class MessageRecyclerViewTouchListener : Java.Lang.Object, AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
{
    private readonly AndroidX.RecyclerView.Widget.RecyclerView _recyclerView;
    private readonly Func<int, ViewModels.ChatMessageItem?> _getItemAtPosition;
    private readonly Action<ViewModels.ChatMessageItem> _onLongPress;
    private readonly int _touchSlop;
    private readonly Android.OS.Handler _handler;
    private Java.Lang.Runnable? _pendingLongPressRunnable;
    private float _downX;
    private float _downY;
    private bool _hasMoved;

    public MessageRecyclerViewTouchListener(
        Android.Content.Context context,
        AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
        Func<int, ViewModels.ChatMessageItem?> getItemAtPosition,
        Action<ViewModels.ChatMessageItem> onLongPress,
        Action? onTouchDown = null)
    {
        _recyclerView = recyclerView;
        _getItemAtPosition = getItemAtPosition;
        _onLongPress = onLongPress;
        _touchSlop = Android.Views.ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16;
        _handler = new Android.OS.Handler(Android.OS.Looper.MainLooper!);
    }

    public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                _downX = e.GetX();
                _downY = e.GetY();
                _hasMoved = false;
                CancelPendingLongPress();

                if (rv.ScrollState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                {
                    float x = _downX;
                    float y = _downY;
                    _pendingLongPressRunnable = new Java.Lang.Runnable(() =>
                    {
                        if (!_hasMoved && rv.ScrollState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                        {
                            HandleLongPressAt(x, y);
                        }
                    });
                    _handler.PostDelayed(_pendingLongPressRunnable, 500);
                }
                break;

            case Android.Views.MotionEventActions.Move:
                if (Math.Abs(e.GetX() - _downX) > _touchSlop || Math.Abs(e.GetY() - _downY) > _touchSlop)
                {
                    _hasMoved = true;
                    CancelPendingLongPress();
                }
                break;

            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                CancelPendingLongPress();
                break;
        }

        return false;
    }

    public void OnTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, Android.Views.MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case Android.Views.MotionEventActions.Move:
                if (Math.Abs(e.GetX() - _downX) > _touchSlop || Math.Abs(e.GetY() - _downY) > _touchSlop)
                {
                    _hasMoved = true;
                    CancelPendingLongPress();
                }
                break;

            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                CancelPendingLongPress();
                break;
        }
    }

    public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
    {
        if (disallowIntercept)
        {
            CancelPendingLongPress();
        }
    }

    private void CancelPendingLongPress()
    {
        if (_pendingLongPressRunnable != null)
        {
            _handler.RemoveCallbacks(_pendingLongPressRunnable);
            _pendingLongPressRunnable = null;
        }
    }

    private void HandleLongPressAt(float x, float y)
    {
        if (_recyclerView.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
        {
            return;
        }

        var child = _recyclerView.FindChildViewUnder(x, y);
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
}

public class OneShotBottomScrollLayoutListener : Java.Lang.Object, Android.Views.View.IOnLayoutChangeListener
{
    private readonly Android.Views.View _view;
    private readonly Action _callback;

    public OneShotBottomScrollLayoutListener(Android.Views.View view, Action callback)
    {
        _view = view;
        _callback = callback;
    }

    public void OnLayoutChange(Android.Views.View? v, int left, int top, int right, int bottom, int oldLeft, int oldTop, int oldRight, int oldBottom)
    {
        _view.RemoveOnLayoutChangeListener(this);
        _callback();
    }

}
#endif
