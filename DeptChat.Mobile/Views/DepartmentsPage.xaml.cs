namespace DeptChat.Mobile.Views;

public partial class DepartmentsPage : ContentPage
{
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
        if (BindingContext is ViewModels.DepartmentsViewModel vm)
        {
            Console.WriteLine($"DEBUG_PAGE: OnAppearing vm found. Initial vm.IsAdmin={vm.IsAdmin}");
            vm.RefreshUserInfo();
            Console.WriteLine($"DEBUG_PAGE: OnAppearing after Refresh. vm.IsAdmin={vm.IsAdmin}");
            await vm.LoadDepartmentsAsync();
        }
        else
        {
            Console.WriteLine($"DEBUG_PAGE: OnAppearing BindingContext is {BindingContext?.GetType().FullName ?? "null"}");
        }
    }
}
