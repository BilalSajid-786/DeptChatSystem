using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DeptChat.Mobile.Services;

namespace DeptChat.Mobile.ViewModels;

public class DepartmentItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public int UnreadCount { get; set; } = 0;
    public bool HasUnread => UnreadCount > 0;
}

public partial class DepartmentsViewModel : ObservableObject
{
    private readonly ChatApiService? _apiService;
    private readonly AuthService? _authService;
    private readonly SignalRService? _signalRService;

    [ObservableProperty]
    private ObservableCollection<DepartmentItem> _departments = new();

    [ObservableProperty]
    private bool _isBusy;

    public string CurrentUserName => _authService?.CurrentUser?.FullName ?? "User";
    public string CurrentUserRole => _authService?.CurrentUser?.Role.ToString() ?? "";
    public string CurrentUserEmail => _authService?.CurrentUser?.Email ?? "";
    public string UserBadge => !string.IsNullOrEmpty(CurrentUserRole) ? $"👤 {CurrentUserName} ({CurrentUserRole})" : "👤 User";

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _isAddDepartmentModalVisible;

    [ObservableProperty]
    private string _newDepartmentName = string.Empty;

    [ObservableProperty]
    private string _newDepartmentDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddDepartmentError))]
    private string _addDepartmentErrorMessage = string.Empty;

    public bool HasAddDepartmentError => !string.IsNullOrWhiteSpace(AddDepartmentErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveDepartment))]
    private bool _isCreatingDepartment;

    public bool CanSaveDepartment => !IsCreatingDepartment;

    public DepartmentsViewModel(ChatApiService apiService, AuthService authService, SignalRService signalRService)
    {
        _apiService = apiService;
        _authService = authService;
        _signalRService = signalRService;

        if (_signalRService != null)
        {
            _signalRService.OnInAppToast += (t) =>
            {
                MainThread.BeginInvokeOnMainThread(async () => await LoadDepartmentsAsync());
            };
            _signalRService.OnMessageReceived += (m) =>
            {
                MainThread.BeginInvokeOnMainThread(async () => await LoadDepartmentsAsync());
            };
            _signalRService.OnMessagesRead += (cId, seq) =>
            {
                MainThread.BeginInvokeOnMainThread(async () => await LoadDepartmentsAsync());
            };
            _signalRService.OnDepartmentAdded += (d) =>
            {
                MainThread.BeginInvokeOnMainThread(async () => await LoadDepartmentsAsync());
            };
            _signalRService.OnDepartmentDeleted += (d) =>
            {
                MainThread.BeginInvokeOnMainThread(async () => await LoadDepartmentsAsync());
            };
        }

        RefreshUserInfo();
        _ = LoadDepartmentsAsync();
    }

    public DepartmentsViewModel()
    {
        LoadDefaultDepartments();
    }

    public void RefreshUserInfo()
    {
        IsAdmin = _authService?.Role == DeptChat.Shared.Enums.UserRole.Admin 
            || string.Equals(_authService?.CurrentUser?.Email, "admin@deptchat.com", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"DEBUG_DEPT: RefreshUserInfo. Email='{_authService?.CurrentUser?.Email}', Role='{_authService?.Role}', IsAdmin={IsAdmin}");

        OnPropertyChanged(nameof(CurrentUserName));
        OnPropertyChanged(nameof(CurrentUserRole));
        OnPropertyChanged(nameof(CurrentUserEmail));
        OnPropertyChanged(nameof(UserBadge));
    }

    [RelayCommand]
    public async Task LoadDepartmentsAsync()
    {
        RefreshUserInfo();

        // Ensure SignalR connection and FCM token registration are active on auto-login
        if (_signalRService != null && !_signalRService.IsConnected && !string.IsNullOrEmpty(_authService?.Token))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _signalRService.ConnectAsync(_authService.Token);
                    var fcmToken = Preferences.Default.Get("fcm_device_token", string.Empty);
                    if (!string.IsNullOrWhiteSpace(fcmToken) && _apiService != null)
                    {
                        await _apiService.RegisterDeviceTokenAsync(fcmToken, "Android");
                    }
                }
                catch { }
            });
        }

        if (_apiService == null)
        {
            LoadDefaultDepartments();
            return;
        }

        try
        {
            IsBusy = true;
            var liveDepts = await _apiService.GetDepartmentsAsync();
            var list = new ObservableCollection<DepartmentItem>();

            foreach (var d in liveDepts)
            {
                var icon = GetDepartmentIcon(d.Name);

                list.Add(new DepartmentItem
                {
                    Id = d.Id,
                    Name = d.Name,
                    Description = d.Description,
                    Icon = icon,
                    UnreadCount = d.UnreadCount
                });
            }

            Departments = list;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _authService?.Logout();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Shell.Current.GoToAsync("//LoginPage");
            });
        }
        catch
        {
            LoadDefaultDepartments();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadDefaultDepartments()
    {
        Departments = new ObservableCollection<DepartmentItem>
        {
            new DepartmentItem
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Name = "Complaints & Feedback",
                Description = "Issues with orders, damaged deliveries, or customer service",
                Icon = "⚠️",
                UnreadCount = 0
            },
            new DepartmentItem
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Name = "IT Support",
                Description = "Password resets, network access, VPN, and hardware issues",
                Icon = "💻",
                UnreadCount = 0
            },
            new DepartmentItem
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Name = "Billing & Accounts",
                Description = "Invoice inquiries, refund requests, and payment updates",
                Icon = "💳",
                UnreadCount = 0
            }
        };
    }

    [RelayCommand]
    private async Task SelectDepartmentAsync(DepartmentItem department)
    {
        if (department == null) return;

        if (_authService?.Role == DeptChat.Shared.Enums.UserRole.Admin || _authService?.Role == DeptChat.Shared.Enums.UserRole.Manager)
        {
            // Staff view: Navigate to list of customer conversations in this department
            await Shell.Current.GoToAsync($"DepartmentConversationsPage?deptId={department.Id}&deptName={Uri.EscapeDataString(department.Name)}");
        }
        else
        {
            // Customer view: Navigates directly to their direct chat room for this department
            await Shell.Current.GoToAsync($"ChatRoomPage?deptId={department.Id}&deptName={Uri.EscapeDataString(department.Name)}");
        }
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        if (_signalRService != null)
        {
            try
            {
                await _signalRService.DisconnectAsync();
            }
            catch
            {
                // Disconnect error ignored during logout
            }
        }

        _authService?.Logout();
        Departments.Clear();

        await Shell.Current.GoToAsync("//LoginPage");
    }

    [RelayCommand]
    private void OpenAddDepartmentModal()
    {
        NewDepartmentName = string.Empty;
        NewDepartmentDescription = string.Empty;
        AddDepartmentErrorMessage = string.Empty;
        IsCreatingDepartment = false;
        IsAddDepartmentModalVisible = true;
    }

    [RelayCommand]
    private void CloseAddDepartmentModal()
    {
        IsAddDepartmentModalVisible = false;
    }

    [RelayCommand]
    private async Task CreateDepartmentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDepartmentName))
        {
            AddDepartmentErrorMessage = "Please enter a department name.";
            return;
        }

        if (_apiService == null)
        {
            AddDepartmentErrorMessage = "API service unavailable.";
            return;
        }

        try
        {
            IsCreatingDepartment = true;
            AddDepartmentErrorMessage = string.Empty;

            var created = await _apiService.CreateDepartmentAsync(NewDepartmentName.Trim(), NewDepartmentDescription.Trim());
            if (created != null)
            {
                IsAddDepartmentModalVisible = false;
                await LoadDepartmentsAsync();
            }
            else
            {
                AddDepartmentErrorMessage = "Failed to create department.";
            }
        }
        catch (Exception ex)
        {
            AddDepartmentErrorMessage = ex.Message;
        }
        finally
        {
            IsCreatingDepartment = false;
        }
    }

    [RelayCommand]
    private async Task DeleteDepartmentAsync(DepartmentItem department)
    {
        if (department == null || !IsAdmin) return;

        bool confirm = await Shell.Current.DisplayAlert(
            "Delete Department",
            $"Are you sure you want to delete '{department.Name}'? Customers will no longer be able to select it.",
            "Delete",
            "Cancel");

        if (!confirm) return;

        if (_apiService != null)
        {
            var success = await _apiService.DeleteDepartmentAsync(department.Id);
            if (success)
            {
                Departments.Remove(department);
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", "Failed to delete department.", "OK");
            }
        }
    }

    private static string GetDepartmentIcon(string name)
    {
        if (name.Contains("Complaint", StringComparison.OrdinalIgnoreCase)) return "⚠️";
        if (name.Contains("IT", StringComparison.OrdinalIgnoreCase) || name.Contains("Tech", StringComparison.OrdinalIgnoreCase)) return "💻";
        if (name.Contains("Billing", StringComparison.OrdinalIgnoreCase) || name.Contains("Account", StringComparison.OrdinalIgnoreCase) || name.Contains("Finance", StringComparison.OrdinalIgnoreCase)) return "💳";
        if (name.Contains("HR", StringComparison.OrdinalIgnoreCase) || name.Contains("Human", StringComparison.OrdinalIgnoreCase)) return "👥";
        if (name.Contains("Sale", StringComparison.OrdinalIgnoreCase) || name.Contains("Marketing", StringComparison.OrdinalIgnoreCase)) return "📈";
        if (name.Contains("Logistic", StringComparison.OrdinalIgnoreCase) || name.Contains("Delivery", StringComparison.OrdinalIgnoreCase) || name.Contains("Ship", StringComparison.OrdinalIgnoreCase)) return "🚚";
        if (name.Contains("Legal", StringComparison.OrdinalIgnoreCase)) return "⚖️";
        if (name.Contains("Support", StringComparison.OrdinalIgnoreCase) || name.Contains("Help", StringComparison.OrdinalIgnoreCase)) return "🎧";
        return "📁";
    }
}
