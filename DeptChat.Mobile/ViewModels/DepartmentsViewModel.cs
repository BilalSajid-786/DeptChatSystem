using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DeptChat.Mobile.Services;
using DeptChat.Shared.Enums;
using DeptChat.Shared.DTOs;

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

public class ManagedAccountItem : ObservableObject
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string AccountType { get; set; } = "Manager"; // "Manager" or "Client"
    private bool _isBlocked;
    public bool IsBlocked
    {
        get => _isBlocked;
        set
        {
            if (SetProperty(ref _isBlocked, value))
            {
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeColor));
                OnPropertyChanged(nameof(StatusBadgeTextColor));
                OnPropertyChanged(nameof(ActionBtnText));
                OnPropertyChanged(nameof(ActionBtnColor));
                OnPropertyChanged(nameof(ActionBtnTextColor));
            }
        }
    }
    public string StatusBadgeText => IsBlocked ? "BLOCKED" : "ACTIVE";
    public string StatusBadgeColor => IsBlocked ? "#FEE2E2" : "#DCFCE7";
    public string StatusBadgeTextColor => IsBlocked ? "#991B1B" : "#166534";
    public string ActionBtnText => IsBlocked ? "Unblock" : "Block";
    public string ActionBtnColor => IsBlocked ? "#DCFCE7" : "#FEE2E2";
    public string ActionBtnTextColor => IsBlocked ? "#166534" : "#DC2626";
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
    public string ClientDisplayName => !string.IsNullOrEmpty(_authService?.CurrentUser?.ClientName) 
        ? _authService.CurrentUser.ClientName 
        : "Main Organization";
    public string CurrentUserPhone => !string.IsNullOrEmpty(_authService?.CurrentUser?.PhoneNumber)
        ? _authService.CurrentUser.PhoneNumber
        : "Not provided";
    public string CurrentUserAddress => !string.IsNullOrEmpty(_authService?.CurrentUser?.Address)
        ? _authService.CurrentUser.Address
        : "Not provided";
    public string UserBadge => !string.IsNullOrEmpty(CurrentUserRole) ? $"👤 {CurrentUserName} ({CurrentUserRole})" : "👤 User";

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _isProfileModalVisible;

    [ObservableProperty]
    private string _editFullName = string.Empty;

    [ObservableProperty]
    private string _editPhoneNumber = string.Empty;

    [ObservableProperty]
    private string _editAddress = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileError))]
    private string _profileErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private bool _isProfileSaving;

    public bool CanSaveProfile => !IsProfileSaving;

    public bool HasProfileError => !string.IsNullOrWhiteSpace(ProfileErrorMessage);

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
    private bool _isAddUserModalVisible;

    [ObservableProperty]
    private string _newUserName = string.Empty;

    [ObservableProperty]
    private string _newUserEmail = string.Empty;

    [ObservableProperty]
    private string _newUserPhone = string.Empty;

    [ObservableProperty]
    private string _newUserPassword = string.Empty;

    [ObservableProperty]
    private string _newUserAddress = string.Empty;

    [ObservableProperty]
    private UserRole _selectedUserRole = UserRole.User;

    [ObservableProperty]
    private bool _isNewUserPasswordHidden = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddUserError))]
    private string _addUserErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveUser))]
    private bool _isAddUserBusy;

    public bool CanSaveUser => !IsAddUserBusy;
    public bool HasAddUserError => !string.IsNullOrWhiteSpace(AddUserErrorMessage);
    public bool IsRoleEmployee => SelectedUserRole == UserRole.User;
    public bool IsRoleManager => SelectedUserRole == UserRole.Manager;
    public bool IsRoleAdmin => SelectedUserRole == UserRole.Admin;
    public string NewUserPasswordToggleIcon => IsNewUserPasswordHidden ? "👁️" : "👁️‍🗨️";

    public string RoleEmployeeBgColor => IsRoleEmployee ? "#008069" : "#F1F5F9";
    public string RoleEmployeeTextColor => IsRoleEmployee ? "White" : "#475569";

    public string RoleManagerBgColor => IsRoleManager ? "#008069" : "#F1F5F9";
    public string RoleManagerTextColor => IsRoleManager ? "White" : "#475569";

    public string RoleAdminBgColor => IsRoleAdmin ? "#008069" : "#F1F5F9";
    public string RoleAdminTextColor => IsRoleAdmin ? "White" : "#475569";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveDepartment))]
    private bool _isCreatingDepartment;

    public bool CanSaveDepartment => !IsCreatingDepartment;

        [ObservableProperty]
    private bool _isManageAccountsModalVisible;

    [ObservableProperty]
    private int _manageAccountsTab; // 0 = Managers, 1 = Users

    public bool IsManagersTabActive => ManageAccountsTab == 0;
    public bool IsUsersTabActive => ManageAccountsTab == 1;

    public string ManagersTabBgColor => IsManagersTabActive ? "#008069" : "#F1F5F9";
    public string ManagersTabTextColor => IsManagersTabActive ? "White" : "#475569";

    public string UsersTabBgColor => IsUsersTabActive ? "#008069" : "#F1F5F9";
    public string UsersTabTextColor => IsUsersTabActive ? "White" : "#475569";

    [ObservableProperty]
    private ObservableCollection<ManagedAccountItem> _managedManagers = new();

    [ObservableProperty]
    private ObservableCollection<ManagedAccountItem> _managedUsers = new();

    private List<ManagedAccountItem> _allUsers = new();

    [ObservableProperty]
    private string _userSearchText = string.Empty;

    partial void OnUserSearchTextChanged(string value)
    {
        FilterUsers();
    }

    private void FilterUsers()
    {
        if (string.IsNullOrWhiteSpace(UserSearchText))
        {
            ManagedUsers = new ObservableCollection<ManagedAccountItem>(_allUsers);
        }
        else
        {
            var q = UserSearchText.Trim().ToLowerInvariant();
            var filtered = _allUsers.Where(u =>
                (u.Title != null && u.Title.ToLowerInvariant().Contains(q)) ||
                (u.Subtitle != null && u.Subtitle.ToLowerInvariant().Contains(q))).ToList();
            ManagedUsers = new ObservableCollection<ManagedAccountItem>(filtered);
        }
    }

    [ObservableProperty]
    private bool _isLoadingAccounts;

    [RelayCommand]
    private void SelectManageAccountsTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
        {
            ManageAccountsTab = idx;
            OnPropertyChanged(nameof(IsManagersTabActive));
            OnPropertyChanged(nameof(IsUsersTabActive));
            OnPropertyChanged(nameof(ManagersTabBgColor));
            OnPropertyChanged(nameof(ManagersTabTextColor));
            OnPropertyChanged(nameof(UsersTabBgColor));
            OnPropertyChanged(nameof(UsersTabTextColor));
        }
    }

    [RelayCommand]
    private async Task OpenManageAccountsModalAsync()
    {
        if (_apiService == null) return;
        IsManageAccountsModalVisible = true;
        IsLoadingAccounts = true;
        ManageAccountsTab = 0;
        OnPropertyChanged(nameof(IsManagersTabActive));
        OnPropertyChanged(nameof(IsUsersTabActive));
        OnPropertyChanged(nameof(ManagersTabBgColor));
        OnPropertyChanged(nameof(ManagersTabTextColor));
        OnPropertyChanged(nameof(UsersTabBgColor));
        OnPropertyChanged(nameof(UsersTabTextColor));

        try
        {
            ManagedManagers.Clear();
            ManagedUsers.Clear();
            _allUsers.Clear();
            UserSearchText = string.Empty;

            var managers = await _apiService.GetManagersAsync();
            foreach (var m in managers)
            {
                ManagedManagers.Add(new ManagedAccountItem
                {
                    Id = m.Id,
                    Title = m.FullName,
                    Subtitle = m.Email,
                    AccountType = "Manager",
                    IsBlocked = m.IsBlocked
                });
            }

            var allAppUsers = await _apiService.GetUsersAsync();
            foreach (var u in allAppUsers.Where(x => x.Role == UserRole.User))
            {
                var item = new ManagedAccountItem
                {
                    Id = u.Id,
                    Title = u.FullName,
                    Subtitle = !string.IsNullOrWhiteSpace(u.Email) ? u.Email : (!string.IsNullOrWhiteSpace(u.PhoneNumber) ? u.PhoneNumber : "Customer"),
                    AccountType = "User",
                    IsBlocked = u.IsBlocked
                };
                _allUsers.Add(item);
                ManagedUsers.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DepartmentsViewModel] OpenManageAccountsModal error: {ex.Message}");
        }
        finally
        {
            IsLoadingAccounts = false;
        }
    }

    [RelayCommand]
    private void CloseManageAccountsModal()
    {
        IsManageAccountsModalVisible = false;
    }

    [RelayCommand]
    private async Task ToggleBlockManagerAsync(ManagedAccountItem? item)
    {
        if (item == null || _apiService == null) return;

        if (item.IsBlocked)
        {
            var (success, msg) = await _apiService.UnblockUserAsync(item.Id);
            if (success)
            {
                item.IsBlocked = false;
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlertAsync("Manager Unblocked", $"{item.Title} has been unblocked.", "OK");
                }
            }
            else if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlertAsync("Error", msg ?? "Failed to unblock manager.", "OK");
            }
        }
        else
        {
            var confirm = Application.Current?.MainPage != null
                ? await Application.Current.MainPage.DisplayAlertAsync("Block Manager", $"Are you sure you want to block {item.Title}? They will not be able to log in or manage department chats.", "Block", "Cancel")
                : false;
            if (confirm)
            {
                var (success, msg) = await _apiService.BlockUserAsync(item.Id, "Blocked by Administrator");
                if (success)
                {
                    item.IsBlocked = true;
                    if (Application.Current?.MainPage != null)
                    {
                        await Application.Current.MainPage.DisplayAlertAsync("Manager Blocked", $"{item.Title} has been blocked.", "OK");
                    }
                }
                else if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlertAsync("Error", msg ?? "Failed to block manager.", "OK");
                }
            }
        }
    }

    [RelayCommand]
    private async Task ToggleBlockUserAsync(ManagedAccountItem? item)
    {
        if (item == null || _apiService == null) return;

        if (item.IsBlocked)
        {
            var (success, msg) = await _apiService.UnblockUserAsync(item.Id);
            if (success)
            {
                item.IsBlocked = false;
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlertAsync("Customer Unblocked", $"{item.Title} has been unblocked.", "OK");
                }
            }
            else if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlertAsync("Error", msg ?? "Failed to unblock customer.", "OK");
            }
        }
        else
        {
            var confirm = Application.Current?.MainPage != null
                ? await Application.Current.MainPage.DisplayAlertAsync("Block Customer", $"Are you sure you want to block {item.Title}? They will not be able to log in or send messages.", "Block Customer", "Cancel")
                : false;
            if (confirm)
            {
                var (success, msg) = await _apiService.BlockUserAsync(item.Id, "Blocked by Administrator");
                if (success)
                {
                    item.IsBlocked = true;
                    if (Application.Current?.MainPage != null)
                    {
                        await Application.Current.MainPage.DisplayAlertAsync("Customer Blocked", $"{item.Title} has been blocked.", "OK");
                    }
                }
                else if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlertAsync("Error", msg ?? "Failed to block customer.", "OK");
                }
            }
        }
    }

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
        OnPropertyChanged(nameof(ClientDisplayName));
        OnPropertyChanged(nameof(CurrentUserPhone));
        OnPropertyChanged(nameof(CurrentUserAddress));
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
        if (_apiService != null)
        {
            try
            {
                await _apiService.LogoutAsync();
            }
            catch
            {
                // Ignore API error during logout
            }
        }

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
    private void OpenProfileModal()
    {
        EditFullName = _authService?.CurrentUser?.FullName ?? string.Empty;
        EditPhoneNumber = _authService?.CurrentUser?.PhoneNumber ?? string.Empty;
        EditAddress = _authService?.CurrentUser?.Address ?? string.Empty;
        ProfileErrorMessage = string.Empty;
        IsProfileSaving = false;
        IsProfileModalVisible = true;
    }

    [RelayCommand]
    private void CloseProfileModal()
    {
        IsProfileModalVisible = false;
        ProfileErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(EditFullName))
        {
            ProfileErrorMessage = "Full Name is required.";
            return;
        }

        if (_authService == null) return;

        try
        {
            IsProfileSaving = true;
            ProfileErrorMessage = string.Empty;
            var success = await _authService.UpdateProfileAsync(EditFullName.Trim(), EditPhoneNumber?.Trim(), EditAddress?.Trim());
            if (success)
            {
                RefreshUserInfo();
                IsProfileModalVisible = false;
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Profile Updated", "Your profile details have been updated successfully.", "OK");
                }
            }
            else
            {
                ProfileErrorMessage = _authService.LastError ?? "Failed to save profile changes.";
            }
        }
        catch (Exception ex)
        {
            ProfileErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProfileSaving = false;
        }
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

    partial void OnSelectedUserRoleChanged(UserRole value)
    {
        OnPropertyChanged(nameof(IsRoleEmployee));
        OnPropertyChanged(nameof(IsRoleManager));
        OnPropertyChanged(nameof(IsRoleAdmin));
        OnPropertyChanged(nameof(RoleEmployeeBgColor));
        OnPropertyChanged(nameof(RoleEmployeeTextColor));
        OnPropertyChanged(nameof(RoleManagerBgColor));
        OnPropertyChanged(nameof(RoleManagerTextColor));
        OnPropertyChanged(nameof(RoleAdminBgColor));
        OnPropertyChanged(nameof(RoleAdminTextColor));
    }

    [RelayCommand]
    private void OpenAddUserModal()
    {
        NewUserName = string.Empty;
        NewUserEmail = string.Empty;
        NewUserPhone = string.Empty;
        NewUserPassword = string.Empty;
        NewUserAddress = string.Empty;
        SelectedUserRole = UserRole.User;
        AddUserErrorMessage = string.Empty;
        IsNewUserPasswordHidden = true;
        IsAddUserModalVisible = true;
    }

    [RelayCommand]
    private void CloseAddUserModal()
    {
        IsAddUserModalVisible = false;
        AddUserErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void SelectUserRole(string roleName)
    {
        if (Enum.TryParse<UserRole>(roleName, out var role))
        {
            SelectedUserRole = role;
        }
    }

    [RelayCommand]
    private void ToggleNewUserPasswordVisibility()
    {
        IsNewUserPasswordHidden = !IsNewUserPasswordHidden;
        OnPropertyChanged(nameof(NewUserPasswordToggleIcon));
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        if (_apiService == null || !IsAdmin) return;

        AddUserErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(NewUserName))
        {
            AddUserErrorMessage = "Full name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewUserEmail) && string.IsNullOrWhiteSpace(NewUserPhone))
        {
            AddUserErrorMessage = "Please enter either an email address or a phone number.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewUserPassword) || NewUserPassword.Length < 6)
        {
            AddUserErrorMessage = "Password must be at least 6 characters long.";
            return;
        }

        try
        {
            IsAddUserBusy = true;
            var request = new CreateUserRequestDto(
                NewUserName.Trim(),
                string.IsNullOrWhiteSpace(NewUserEmail) ? null : NewUserEmail.Trim(),
                string.IsNullOrWhiteSpace(NewUserPhone) ? null : NewUserPhone.Trim(),
                NewUserPassword,
                SelectedUserRole,
                string.IsNullOrWhiteSpace(NewUserAddress) ? null : NewUserAddress.Trim()
            );

            var (success, errorMsg, user) = await _apiService.CreateUserAsync(request);
            if (success && user != null)
            {
                IsAddUserModalVisible = false;
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("User Created", $"Successfully added {user.FullName} ({user.Role}) to your organization.", "OK");
                }
            }
            else
            {
                AddUserErrorMessage = errorMsg ?? "Failed to create user.";
            }
        }
        catch (Exception ex)
        {
            AddUserErrorMessage = ex.Message;
        }
        finally
        {
            IsAddUserBusy = false;
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
