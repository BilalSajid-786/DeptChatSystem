using DeptChat.Mobile.Services;

namespace DeptChat.Mobile.Views;

public partial class DepartmentsPage : ContentPage
{
    // Set when we return to a list that is already on screen: the reload then waits
    // until the back animation has finished (OnNavigatedTo), so it no longer stalls it.
    private bool _reloadAfterNavigation;

    public DepartmentsPage(ViewModels.DepartmentsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    public DepartmentsPage()
    {
        InitializeComponent();
        BindingContext = IPlatformApplication.Current?.Services.GetService<ViewModels.DepartmentsViewModel>()
            ?? new ViewModels.DepartmentsViewModel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        PerfTrace.LeaveStep("Departments appearing");
        _ = App.CheckAndEnforceUpdateAsync();

        if (BindingContext is ViewModels.DepartmentsViewModel vm)
        {
            vm.IsPageVisible = true;
            Console.WriteLine($"DEBUG_PAGE: OnAppearing vm found. Initial vm.IsAdmin={vm.IsAdmin}");
            vm.RefreshUserInfo();
            vm.CheckPendingShare();
            Console.WriteLine($"DEBUG_PAGE: OnAppearing after Refresh. vm.IsAdmin={vm.IsAdmin}");

            if (vm.HasListForCurrentUser)
            {
                // Returning (e.g. Back from a department): this user's list is already showing.
                _reloadAfterNavigation = true;

                // Safety net in case OnNavigatedTo does not arrive.
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), ReloadAfterNavigation);
                return;
            }

            await vm.LoadDepartmentsAsync();
        }
        else
        {
            Console.WriteLine($"DEBUG_PAGE: OnAppearing BindingContext is {BindingContext?.GetType().FullName ?? "null"}");
        }
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        PerfTrace.LeaveStep("Departments navigated to (back animation done)");
        ReloadAfterNavigation();
    }

    private async void ReloadAfterNavigation()
    {
        if (!_reloadAfterNavigation) return;
        _reloadAfterNavigation = false;

        if (BindingContext is ViewModels.DepartmentsViewModel vm && vm.IsPageVisible)
        {
            PerfTrace.LeaveStep("Departments reload started");
            await vm.LoadDepartmentsAsync();
            PerfTrace.LeaveStep("Departments reload finished");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _reloadAfterNavigation = false;
        if (BindingContext is ViewModels.DepartmentsViewModel vm)
        {
            // Pauses background reloads; OnAppearing reloads when the user comes back.
            vm.IsPageVisible = false;
        }
    }
}
