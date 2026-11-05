namespace DeptChat.Mobile.Views;

public partial class DepartmentConversationsPage : ContentPage
{
    public DepartmentConversationsPage(ViewModels.DepartmentConversationsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    public DepartmentConversationsPage()
    {
        InitializeComponent();
        BindingContext = new ViewModels.DepartmentConversationsViewModel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ViewModels.DepartmentConversationsViewModel vm)
        {
            vm.ShowInAppToast = false;
            vm.IsShareModeBanner = !string.IsNullOrEmpty(vm.SharedFilePath) || Services.IncomingShareService.Instance.HasPendingShare;
            await vm.LoadConversationsAsync();
        }
    }
}
