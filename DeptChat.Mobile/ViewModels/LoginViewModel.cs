using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeptChat.Mobile.Services;

namespace DeptChat.Mobile.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService? _authService;
    private readonly SignalRService? _signalRService;
    private readonly ChatApiService? _chatApiService;
    private readonly ServerConfigService? _serverConfig;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _email = "john@example.com";

    [ObservableProperty]
    private string _password = "Password123!";

    [ObservableProperty]
    private bool _isPasswordHidden = true;

    [ObservableProperty]
    private string _fullName = "John Doe";

    [ObservableProperty]
    private bool _isRegisterMode;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public string SubmitButtonText => IsRegisterMode ? "Create Account" : "Sign In";
    public string ToggleModeText => IsRegisterMode ? "Already have an account? Sign In" : "Need an account? Register";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string PasswordToggleIcon => IsPasswordHidden ? "👁️" : "👁️‍🗨️";

    public LoginViewModel(AuthService authService, SignalRService signalRService, ChatApiService chatApiService, ServerConfigService serverConfig)
    {
        _authService = authService;
        _signalRService = signalRService;
        _chatApiService = chatApiService;
        _serverConfig = serverConfig;
        _serverUrl = _serverConfig?.CurrentUrl ?? ServerConfigService.DefaultUrl;
    }

    public LoginViewModel()
    {
        _serverUrl = ServerConfigService.DefaultUrl;
    }

    public async Task CheckAutoLoginAsync()
    {
        if (_authService != null && _authService.IsAuthenticated)
        {
            var token = _authService.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    var fcmToken = Preferences.Default.Get("fcm_device_token", string.Empty);
                    if (!string.IsNullOrWhiteSpace(fcmToken) && _chatApiService != null)
                    {
                        await _chatApiService.RegisterDeviceTokenAsync(fcmToken, "Android");
                    }

                    if (_signalRService != null && !string.IsNullOrEmpty(token))
                    {
                        await _signalRService.ConnectAsync(token);
                    }
                }
                catch { }
            });

            await Shell.Current.GoToAsync("//DepartmentsPage");
        }
    }

    partial void OnIsRegisterModeChanged(bool value)
    {
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(ToggleModeText));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnIsPasswordHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordToggleIcon));
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordHidden = !IsPasswordHidden;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = string.Empty;

        if (_serverConfig != null && !string.IsNullOrWhiteSpace(ServerUrl))
        {
            _serverConfig.CurrentUrl = ServerUrl;
            ServerUrl = _serverConfig.CurrentUrl;
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter both email and password.";
            return;
        }

        if (_authService == null)
        {
            await Shell.Current.GoToAsync("//DepartmentsPage");
            return;
        }

        try
        {
            IsBusy = true;
            bool success;

            if (IsRegisterMode)
            {
                success = await _authService.RegisterAsync(FullName, Email, Password);
            }
            else
            {
                success = await _authService.LoginAsync(Email, Password);
            }

            if (success)
            {
                var token = _authService.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var fcmToken = Preferences.Default.Get("fcm_device_token", string.Empty);
                        if (!string.IsNullOrWhiteSpace(fcmToken) && _chatApiService != null)
                        {
                            await _chatApiService.RegisterDeviceTokenAsync(fcmToken, "Android");
                        }

                        if (_signalRService != null && !string.IsNullOrEmpty(token))
                        {
                            await _signalRService.ConnectAsync(token);
                        }
                    }
                    catch { }
                });

                await Shell.Current.GoToAsync("//DepartmentsPage");
            }
            else
            {
                ErrorMessage = _authService.LastError ?? (IsRegisterMode ? "Registration failed. Email may already be in use." : "Invalid email or password.");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task QuickLoginCustomerAsync()
    {
        Email = "john@example.com";
        Password = "Password123!";
        IsRegisterMode = false;
        await SubmitAsync();
    }

    [RelayCommand]
    private async Task QuickLoginManagerAsync()
    {
        Email = "manager@deptchat.com";
        Password = "Manager123!";
        IsRegisterMode = false;
        await SubmitAsync();
    }

    [RelayCommand]
    private async Task QuickLoginAdminAsync()
    {
        Email = "admin@deptchat.com";
        Password = "Admin123!";
        IsRegisterMode = false;
        await SubmitAsync();
    }

    [RelayCommand]
    private async Task QuickLoginU100Async()
    {
        Email = "u100@example.com";
        Password = "Password123!";
        IsRegisterMode = false;
        await SubmitAsync();
    }

    public bool IsLocalWifiActive => ServerUrl.Contains("192.168.");

    partial void OnServerUrlChanged(string value)
    {
        OnPropertyChanged(nameof(IsLocalWifiActive));
    }

    [RelayCommand]
    private async Task SaveServerUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl)) return;
        if (_serverConfig != null)
        {
            _serverConfig.CurrentUrl = ServerUrl;
            ServerUrl = _serverConfig.CurrentUrl;
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert("Server Configured", $"Backend updated to:\n{_serverConfig.CurrentUrl}", "OK");
            }
        }
    }

    [RelayCommand]
    private async Task UseLocalWifiAsync()
    {
        ServerUrl = ServerConfigService.LocalWifiUrl;
        if (_serverConfig != null)
        {
            _serverConfig.CurrentUrl = ServerUrl;
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert("Local Wi-Fi Mode", $"Switched to local Wi-Fi:\n{ServerConfigService.LocalWifiUrl}", "OK");
            }
        }
    }

    [RelayCommand]
    private async Task UseCloudflareAsync()
    {
        ServerUrl = ServerConfigService.CloudflareUrl;
        if (_serverConfig != null)
        {
            _serverConfig.CurrentUrl = ServerUrl;
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert("Cloudflare Tunnel Mode", $"Switched to Cloudflare Tunnel:\n{ServerConfigService.CloudflareUrl}", "OK");
            }
        }
    }
}
