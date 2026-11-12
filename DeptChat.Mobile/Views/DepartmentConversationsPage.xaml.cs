using DeptChat.Mobile.Services;

namespace DeptChat.Mobile.Views;

public partial class DepartmentConversationsPage : ContentPage
{
    public DepartmentConversationsPage(ViewModels.DepartmentConversationsViewModel vm)
    {
        PerfTrace.Step("Page constructing");
        InitializeComponent();
        BindingContext = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        PerfTrace.Step("Page constructed");
#if DEPTCHAT_TIMING
        AddTimingOverlay();
        Loaded += (_, _) => PerfTrace.Step("Page loaded (native views ready)");
#endif
    }

    public DepartmentConversationsPage()
    {
        InitializeComponent();
        BindingContext = new ViewModels.DepartmentConversationsViewModel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        PerfTrace.Step("Page appearing");
#if DEPTCHAT_TIMING
        StartTimingOverlay();
#endif
        if (BindingContext is ViewModels.DepartmentConversationsViewModel vm)
        {
            vm.ShowInAppToast = false;
            vm.IsShareModeBanner = !string.IsNullOrEmpty(vm.SharedFilePath) || Services.IncomingShareService.Instance.HasPendingShare;
            vm.SubscribeToEvents();

            // Safety net: rows are normally shown in OnNavigatedTo (after the slide-in);
            // if that event does not arrive for any reason, show them shortly anyway.
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), vm.ShowRows);

            await vm.LoadConversationsAsync();
        }
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        PerfTrace.Step("Navigated to (slide-in done)");

        // The page slides in first; its rows are created only after that, so building
        // the rows no longer delays the page from appearing after a tap.
        if (BindingContext is ViewModels.DepartmentConversationsViewModel vm)
        {
            vm.ShowRows();
        }
    }

    // The start-a-chat popup is built the first time it is opened, not on every page open.
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.DepartmentConversationsViewModel.IsSelectUserModalVisible)
            && sender is ViewModels.DepartmentConversationsViewModel vm
            && vm.IsSelectUserModalVisible
            && StartChatModalHost.Content == null)
        {
            StartChatModalHost.Content = new StartChatModalView();
        }
    }

    // Row tap. Handled here instead of a per-row RelativeSource binding, which re-searched
    // the page for the command every time a row was reused while scrolling.
    private void OnConversationTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject row
            && row.BindingContext is ViewModels.ConversationListItem item
            && BindingContext is ViewModels.DepartmentConversationsViewModel vm
            && vm.SelectConversationCommand.CanExecute(item))
        {
            vm.SelectConversationCommand.Execute(item);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if DEPTCHAT_TIMING
        StopTimingOverlay();
#endif
        PerfTrace.MarkLeave("customer list");
        if (BindingContext is ViewModels.DepartmentConversationsViewModel vm)
        {
            vm.UnsubscribeFromEvents();
        }
    }

#if DEPTCHAT_TIMING
    // ---------------------------------------------------------------------------------
    // Internal timing overlay. Compiled only with -p:DeptChatTiming=true.
    // Tap the overlay to copy its text to the clipboard.
    // ---------------------------------------------------------------------------------
    private Label? _timingLabel;
    private IDispatcherTimer? _timingTimer;

    private void AddTimingOverlay()
    {
        if (Content is not Grid root) return;

        _timingLabel = new Label
        {
            FontFamily = "monospace",
            FontSize = 10,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#CC000000"),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(8, 0, 8, 90),
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 330,
            ZIndex = 100
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try
            {
                await Clipboard.Default.SetTextAsync(PerfTrace.Snapshot());
                _timingLabel.Text = "Copied to clipboard ✓\n" + PerfTrace.Snapshot();
            }
            catch { }
        };
        _timingLabel.GestureRecognizers.Add(tap);

        Grid.SetRowSpan(_timingLabel, Math.Max(1, root.RowDefinitions.Count));
        root.Children.Add(_timingLabel);
    }

    private void StartTimingOverlay()
    {
        PerfTrace.Changed += OnTimingChanged;
        _timingTimer ??= Dispatcher.CreateTimer();
        _timingTimer.Interval = TimeSpan.FromSeconds(1);
        _timingTimer.Tick -= OnTimingTick;
        _timingTimer.Tick += OnTimingTick;
        _timingTimer.Start();
        PerfTrace.StartFrames();
        RefreshTimingLabel();
    }

    private void StopTimingOverlay()
    {
        PerfTrace.Changed -= OnTimingChanged;
        _timingTimer?.Stop();
        PerfTrace.StopFrames();
    }

    private void OnTimingTick(object? sender, EventArgs e) => RefreshTimingLabel();

    private void OnTimingChanged() => MainThread.BeginInvokeOnMainThread(RefreshTimingLabel);

    private void RefreshTimingLabel()
    {
        if (_timingLabel != null) _timingLabel.Text = PerfTrace.Snapshot();
    }

#endif
}
