namespace DeptChat.Mobile.Views;

public partial class ChatRoomPage : ContentPage
{
    private ViewModels.ChatRoomViewModel? _vm;

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
    }

    private void OnRequestScrollToBottom()
    {
        ScrollToBottom(animate: true);
    }

    private void ScrollToBottom(bool animate = true)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Delay(80);
                if (_vm?.Messages.Count > 0)
                {
                    var lastIndex = _vm.Messages.Count - 1;
                    MessagesCollectionView.ScrollTo(lastIndex, position: ScrollToPosition.End, animate: animate);
                }
            }
            catch { }
        });
    }

    private void OnMessageEntryFocused(object? sender, FocusEventArgs e)
    {
        if (e.IsFocused)
        {
            // Give the keyboard animation time to adjust viewport height
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(250);
                if (_vm?.Messages.Count > 4)
                {
                    ScrollToBottom(animate: true);
                }
            });
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.SetSoftInputMode(Android.Views.SoftInput.AdjustResize);
#endif
        if (BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            _vm = vm;
            AttachScrollHandler();
            await vm.InitializeAsync();

            // Auto-scroll to the very last message when opening a chat
            ScrollToBottom(animate: false);

            // Secondary pass after layout stabilization
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(250);
                ScrollToBottom(animate: false);
            });
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ViewModels.ChatRoomViewModel vm)
        {
            vm.RequestScrollToBottom -= OnRequestScrollToBottom;
            await vm.OnDisappearingAsync();
        }
    }
}
