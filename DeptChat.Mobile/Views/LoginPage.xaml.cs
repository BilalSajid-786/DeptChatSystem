using DeptChat.Mobile.ViewModels;

namespace DeptChat.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    public LoginPage()
    {
        InitializeComponent();
        BindingContext = new LoginViewModel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is LoginViewModel vm)
        {
            await vm.CheckAutoLoginAsync();
        }
    }
}
