using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;

using DeptChat.Mobile.Services;

using DeptChat.Shared.Enums;

using DeptChat.Shared.DTOs;



namespace DeptChat.Mobile.ViewModels;



public class DepartmentItem : ObservableObject

{

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Icon { get; set; } = "📁";

    public int UnreadCount { get; set; } = 0;

    public bool HasUnread => UnreadCount > 0;



    public string? LastMessageSenderName { get; set; }

    public string? LastMessagePreview { get; set; }

    public DateTime? LastMessageAt { get; set; }



    public bool HasRecentMessage => !string.IsNullOrWhiteSpace(LastMessagePreview);



    public string SubtitleText => HasRecentMessage

        ? (!string.IsNullOrWhiteSpace(LastMessageSenderName) ? $"{LastMessageSenderName}: {LastMessagePreview}" : LastMessagePreview!)

        : Description;



    public string TimeFormatted

    {

        get

        {

            if (!LastMessageAt.HasValue) return string.Empty;

            var local = LastMessageAt.Value.ToLocalTime();

            var today = DateTime.Today;

            if (local.Date == today)

                return local.ToString("h:mm tt");

            if (local.Date == today.AddDays(-1))

                return "Yesterday";

            if (local.Date > today.AddDays(-7))

                return local.ToString("dddd");

            return local.ToString("M/d/yy");

        }

    }



    public Color TimestampColor => HasUnread ? Color.FromArgb("#25D366") : Color.FromArgb("#667781");

    public Color SubtitleTextColor => HasUnread ? Color.FromArgb("#111B21") : Color.FromArgb("#667781");

    public FontAttributes SubtitleFontAttributes => HasUnread ? FontAttributes.Bold : FontAttributes.None;

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
    private readonly LocalOutboxService? _outboxService;



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

    // --- Self-Service Change Password Properties ---
    [ObservableProperty]
    private string _changePasswordCurrent = string.Empty;

    [ObservableProperty]
    private string _changePasswordNew = string.Empty;

    [ObservableProperty]
    private string _changePasswordConfirm = string.Empty;

    [ObservableProperty]
    private bool _isChangePasswordSectionVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangePasswordError))]
    private string _changePasswordErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveChangePassword))]
    private bool _isChangePasswordSaving;

    public bool HasChangePasswordError => !string.IsNullOrWhiteSpace(ChangePasswordErrorMessage);
    public bool CanSaveChangePassword => !IsChangePasswordSaving;

    // --- Admin Reset Password Properties ---
    [ObservableProperty]
    private bool _isAdminResetPasswordModalVisible;

    [ObservableProperty]
    private ManagedAccountItem? _selectedAccountForReset;

    [ObservableProperty]
    private string _adminResetNewPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdminResetError))]
    private string _adminResetErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveAdminReset))]
    private bool _isAdminResetSaving;

    public bool HasAdminResetError => !string.IsNullOrWhiteSpace(AdminResetErrorMessage);
    public bool CanSaveAdminReset => !IsAdminResetSaving;





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

    private string _newUserUsername = string.Empty;



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



    public DepartmentsViewModel(ChatApiService apiService, AuthService authService, SignalRService signalRService, LocalOutboxService? outboxService = null)

    {

        _apiService = apiService;

        _authService = authService;

        _signalRService = signalRService;
        _outboxService = outboxService;



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



        IncomingShareService.Instance.OnPendingShareReceived += (path) =>
        {
            MainThread.BeginInvokeOnMainThread(CheckPendingShare);
        };

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

                    UnreadCount = d.UnreadCount,

                    LastMessageSenderName = d.LastMessageSenderName,

                    LastMessagePreview = d.LastMessagePreview,

                    LastMessageAt = d.LastMessageAt

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



    // ==========================================
    // External Share Workflow Properties & State
    // ==========================================
    [ObservableProperty]
    private bool _isShareDeptModalVisible;

    [ObservableProperty]
    private bool _isShareUserModalVisible;

    [ObservableProperty]
    private bool _isShareSending;

    [ObservableProperty]
    private string _shareSendingStatus = "Sending receipt...";

    [ObservableProperty]
    private DepartmentItem? _selectedShareDepartment;

    [ObservableProperty]
    private string _selectedShareDeptName = string.Empty;

    [ObservableProperty]
    private string _shareUserSearchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoadingShareUsers;

    [ObservableProperty]
    private bool _showNoShareUsersFound;

    [ObservableProperty]
    private ObservableCollection<AvailableUserItem> _shareUsers = new();

    private List<AvailableUserItem> _allShareUsers = new();

    partial void OnShareUserSearchQueryChanged(string value)
    {
        FilterShareUsers();
    }

    private void FilterShareUsers()
    {
        var query = ShareUserSearchQuery?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            ShareUsers = new ObservableCollection<AvailableUserItem>(_allShareUsers);
        }
        else
        {
            var filtered = _allShareUsers.Where(u =>
                (!string.IsNullOrEmpty(u.FullName) && u.FullName.ToLowerInvariant().Contains(query)) ||
                (!string.IsNullOrEmpty(u.Email) && u.Email.ToLowerInvariant().Contains(query))
            ).ToList();
            ShareUsers = new ObservableCollection<AvailableUserItem>(filtered);
        }
        ShowNoShareUsersFound = ShareUsers.Count == 0 && !IsLoadingShareUsers;
    }

    public void CheckPendingShare()
    {
        if (IncomingShareService.Instance.HasPendingShare)
        {
            IsShareDeptModalVisible = true;
            IsShareUserModalVisible = false;
            IsShareSending = false;
        }
        else
        {
            IsShareDeptModalVisible = false;
            IsShareUserModalVisible = false;
            IsShareSending = false;
        }
    }

    [RelayCommand]
    private void CancelShareWorkflow()
    {
        IsShareDeptModalVisible = false;
        IsShareUserModalVisible = false;
        IsShareSending = false;
        IncomingShareService.Instance.ClearPendingShare();
    }

    [RelayCommand]
    private void BackToShareDeptModal()
    {
        IsShareUserModalVisible = false;
        IsShareDeptModalVisible = true;
    }

    [RelayCommand]
    private async Task SelectShareDepartmentAsync(DepartmentItem dept)
    {
        if (dept == null) return;
        SelectedShareDepartment = dept;
        SelectedShareDeptName = dept.Name;

        if (_authService?.Role == DeptChat.Shared.Enums.UserRole.Admin || _authService?.Role == DeptChat.Shared.Enums.UserRole.Manager)
        {
            IsShareDeptModalVisible = false;
            IsShareUserModalVisible = true;
            await LoadShareUsersAsync(dept.Id);
        }
        else
        {
            IsShareDeptModalVisible = false;
            await ExecuteInstantSendCustomerAsync(dept);
        }
    }

    private async Task LoadShareUsersAsync(Guid deptId)
    {
        if (_apiService == null) return;

        try
        {
            IsLoadingShareUsers = true;
            ShowNoShareUsersFound = false;
            _allShareUsers.Clear();
            ShareUsers.Clear();

            // 1. Fetch existing active conversations in this department
            var existingConvsTask = _apiService.GetConversationsForDepartmentAsync(deptId);

            // 2. Fetch all other registered customers
            var availableUsersTask = _apiService.GetAvailableUsersForDepartmentAsync(deptId);

            await Task.WhenAll(existingConvsTask, availableUsersTask);

            var existingConvs = existingConvsTask.Result ?? new List<ConversationSummaryDto>();
            var availableUsers = availableUsersTask.Result ?? new List<AvailableDepartmentUserDto>();

            var seenUserIds = new HashSet<Guid>();

            // Prioritize customers with existing conversations in this department
            foreach (var conv in existingConvs)
            {
                if (conv.CreatedByUserId != Guid.Empty && seenUserIds.Add(conv.CreatedByUserId))
                {
                    _allShareUsers.Add(new AvailableUserItem
                    {
                        Id = conv.CreatedByUserId,
                        FullName = conv.CreatedByUserName,
                        Email = !string.IsNullOrWhiteSpace(conv.LastMessagePreview)
                            ? conv.LastMessagePreview
                            : "Active Chat",
                        Role = UserRole.User,
                        HasExistingChat = true,
                        IsOnline = conv.IsOnline,
                        IsBlocked = conv.IsUserBlocked
                    });
                }
            }

            // Append any additional registered customers
            foreach (var u in availableUsers)
            {
                if (seenUserIds.Add(u.Id))
                {
                    _allShareUsers.Add(new AvailableUserItem
                    {
                        Id = u.Id,
                        FullName = u.FullName,
                        Email = u.Email ?? "",
                        Role = u.Role,
                        HasExistingChat = u.HasExistingChat,
                        IsOnline = u.IsOnline,
                        IsBlocked = u.IsBlocked
                    });
                }
            }

            FilterShareUsers();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DepartmentsViewModel] LoadShareUsersAsync error: {ex.Message}");
            ShowNoShareUsersFound = true;
        }
        finally
        {
            IsLoadingShareUsers = false;
            ShowNoShareUsersFound = ShareUsers.Count == 0;
        }
    }

    [RelayCommand]
    private async Task SelectShareUserAndSendAsync(AvailableUserItem user)
    {
        if (user == null || SelectedShareDepartment == null) return;
        await ExecuteInstantSendAdminAsync(SelectedShareDepartment, user);
    }

    private async Task ExecuteInstantSendAdminAsync(DepartmentItem dept, AvailableUserItem user)
    {
        if (_apiService == null) return;

        var filePath = IncomingShareService.Instance.PendingSharedFilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            CancelShareWorkflow();
            return;
        }

        try
        {
            IsShareUserModalVisible = false;
            IsShareSending = true;
            ShareSendingStatus = $"Sending receipt to {user.FullName}...";

            // 1. Ensure conversation with user
            var conv = await _apiService.StartConversationWithUserAsync(dept.Id, user.Id);
            if (conv == null)
            {
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlert("Error", "Could not start conversation with this user.", "OK");
                }
                return;
            }

            // 2. Upload file
            var fileName = IncomingShareService.Instance.PendingSharedFileName ?? Path.GetFileName(filePath);
            var mimeType = IncomingShareService.Instance.PendingSharedMimeType ?? "image/jpeg";
            AttachmentUploadResultDto? uploadResult;
            using (var fileStream = File.OpenRead(filePath))
            {
                uploadResult = await _apiService.UploadAttachmentAsync(fileStream, fileName, mimeType);
            }

            if (uploadResult == null)
            {
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlert("Upload Failed", "Could not upload the shared receipt. Please try again.", "OK");
                }
                return;
            }

            // 3. Build payload & client message ID
            var isImage = mimeType.StartsWith("image/") || 
                          fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                          fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                          fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                          fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

            var payload = isImage ? $"[image:{uploadResult.Url}]" : $"[file:{uploadResult.Url}:{fileName}:{new FileInfo(filePath).Length}]";
            var clientMsgId = Guid.NewGuid().ToString();

            // 4. Save to outbox
            if (_outboxService != null)
            {
                var myName = _authService?.CurrentUser?.FullName ?? "Admin";
                var myUserId = _authService?.UserId ?? Guid.Empty;
                var myRole = _authService?.Role ?? UserRole.Admin;
                await _outboxService.EnqueueAsync(conv.Id, clientMsgId, payload, myName, myUserId, myRole);
            }

            // 5. Send message via SignalR
            if (_signalRService != null)
            {
                await _signalRService.SendMessageAsync(conv.Id, clientMsgId, payload);
            }

            // 6. Clear pending share and navigate to chat room
            IncomingShareService.Instance.ClearPendingShare();

            await Shell.Current.GoToAsync($"ChatRoomPage?deptId={conv.DepartmentId}&deptName={Uri.EscapeDataString(dept.Name)}&convId={conv.Id}&userName={Uri.EscapeDataString(user.FullName)}&userId={user.Id}&isOnline={user.IsOnline}&isBlocked={user.IsBlocked}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DepartmentsViewModel] ExecuteInstantSendAdminAsync error: {ex.Message}");
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert("Error", $"Failed to send shared receipt: {ex.Message}", "OK");
            }
        }
        finally
        {
            IsShareSending = false;
        }
    }

    private async Task ExecuteInstantSendCustomerAsync(DepartmentItem dept)
    {
        if (_apiService == null) return;

        var filePath = IncomingShareService.Instance.PendingSharedFilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            CancelShareWorkflow();
            return;
        }

        try
        {
            IsShareDeptModalVisible = false;
            IsShareSending = true;
            ShareSendingStatus = $"Sending receipt to {dept.Name}...";

            // 1. Get or find customer conversation in this department
            var convs = await _apiService.GetConversationsForDepartmentAsync(dept.Id);
            var conv = convs?.FirstOrDefault();
            if (conv == null)
            {
                // Fallback: If no existing conversation, pass to ChatRoomPage to initialize
                IncomingShareService.Instance.ClearPendingShare();
                await Shell.Current.GoToAsync($"ChatRoomPage?deptId={dept.Id}&deptName={Uri.EscapeDataString(dept.Name)}&sharedFilePath={Uri.EscapeDataString(filePath)}");
                return;
            }

            // 2. Upload file
            var fileName = IncomingShareService.Instance.PendingSharedFileName ?? Path.GetFileName(filePath);
            var mimeType = IncomingShareService.Instance.PendingSharedMimeType ?? "image/jpeg";
            AttachmentUploadResultDto? uploadResult;
            using (var fileStream = File.OpenRead(filePath))
            {
                uploadResult = await _apiService.UploadAttachmentAsync(fileStream, fileName, mimeType);
            }

            if (uploadResult == null)
            {
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlert("Upload Failed", "Could not upload the shared receipt. Please try again.", "OK");
                }
                return;
            }

            var isImage = mimeType.StartsWith("image/") || 
                          fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                          fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                          fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                          fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

            var payload = isImage ? $"[image:{uploadResult.Url}]" : $"[file:{uploadResult.Url}:{fileName}:{new FileInfo(filePath).Length}]";
            var clientMsgId = Guid.NewGuid().ToString();

            if (_outboxService != null)
            {
                var myName = _authService?.CurrentUser?.FullName ?? "Customer";
                var myUserId = _authService?.UserId ?? Guid.Empty;
                var myRole = _authService?.Role ?? UserRole.User;
                await _outboxService.EnqueueAsync(conv.Id, clientMsgId, payload, myName, myUserId, myRole);
            }

            if (_signalRService != null)
            {
                await _signalRService.SendMessageAsync(conv.Id, clientMsgId, payload);
            }

            IncomingShareService.Instance.ClearPendingShare();

            await Shell.Current.GoToAsync($"ChatRoomPage?deptId={dept.Id}&deptName={Uri.EscapeDataString(dept.Name)}&convId={conv.Id}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DepartmentsViewModel] ExecuteInstantSendCustomerAsync error: {ex.Message}");
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert("Error", $"Failed to send shared receipt: {ex.Message}", "OK");
            }
        }
        finally
        {
            IsShareSending = false;
        }
    }

    [RelayCommand]
    private async Task SelectDepartmentAsync(DepartmentItem department)
    {
        if (department == null) return;

        if (IncomingShareService.Instance.HasPendingShare)
        {
            await SelectShareDepartmentAsync(department);
            return;
        }

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

        NewUserUsername = string.Empty;

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



        if (string.IsNullOrWhiteSpace(NewUserUsername) && string.IsNullOrWhiteSpace(NewUserEmail) && string.IsNullOrWhiteSpace(NewUserPhone))

        {

            AddUserErrorMessage = "Please enter a username, an email address, or a phone number.";

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

                string.IsNullOrWhiteSpace(NewUserAddress) ? null : NewUserAddress.Trim(),

                string.IsNullOrWhiteSpace(NewUserUsername) ? null : NewUserUsername.Trim()

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



    [RelayCommand]
    private void ToggleChangePasswordSection()
    {
        IsChangePasswordSectionVisible = !IsChangePasswordSectionVisible;
        ChangePasswordCurrent = string.Empty;
        ChangePasswordNew = string.Empty;
        ChangePasswordConfirm = string.Empty;
        ChangePasswordErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveChangePasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(ChangePasswordCurrent))
        {
            ChangePasswordErrorMessage = "Current password is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ChangePasswordNew) || ChangePasswordNew.Length < 6)
        {
            ChangePasswordErrorMessage = "New password must be at least 6 characters long.";
            return;
        }
        if (ChangePasswordNew != ChangePasswordConfirm)
        {
            ChangePasswordErrorMessage = "New password and confirmation do not match.";
            return;
        }
        if (ChangePasswordCurrent == ChangePasswordNew)
        {
            ChangePasswordErrorMessage = "New password cannot be the same as your current password.";
            return;
        }
        if (_authService == null) return;

        try
        {
            IsChangePasswordSaving = true;
            ChangePasswordErrorMessage = string.Empty;
            var (success, msg) = await _authService.ChangePasswordAsync(ChangePasswordCurrent, ChangePasswordNew);
            if (success)
            {
                ChangePasswordCurrent = string.Empty;
                ChangePasswordNew = string.Empty;
                ChangePasswordConfirm = string.Empty;
                IsChangePasswordSectionVisible = false;
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlertAsync("Password Updated", "Your password has been changed successfully.", "OK");
                }
            }
            else
            {
                ChangePasswordErrorMessage = msg ?? "Failed to change password.";
            }
        }
        catch (Exception ex)
        {
            ChangePasswordErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsChangePasswordSaving = false;
        }
    }

    [RelayCommand]
    private void OpenAdminResetPasswordModal(ManagedAccountItem? item)
    {
        if (item == null) return;
        SelectedAccountForReset = item;
        AdminResetNewPassword = string.Empty;
        AdminResetErrorMessage = string.Empty;
        IsAdminResetSaving = false;
        IsAdminResetPasswordModalVisible = true;
    }

    [RelayCommand]
    private void CloseAdminResetPasswordModal()
    {
        IsAdminResetPasswordModalVisible = false;
        SelectedAccountForReset = null;
        AdminResetNewPassword = string.Empty;
        AdminResetErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmAdminResetPasswordAsync()
    {
        if (SelectedAccountForReset == null || _apiService == null) return;
        if (string.IsNullOrWhiteSpace(AdminResetNewPassword) || AdminResetNewPassword.Length < 6)
        {
            AdminResetErrorMessage = "New password must be at least 6 characters long.";
            return;
        }

        try
        {
            IsAdminResetSaving = true;
            AdminResetErrorMessage = string.Empty;
            var (success, msg) = await _apiService.AdminResetPasswordAsync(SelectedAccountForReset.Id, AdminResetNewPassword);
            if (success)
            {
                var title = SelectedAccountForReset.Title;
                IsAdminResetPasswordModalVisible = false;
                SelectedAccountForReset = null;
                AdminResetNewPassword = string.Empty;
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlertAsync("Password Reset", $"Password for {title} has been reset successfully.", "OK");
                }
            }
            else
            {
                AdminResetErrorMessage = msg ?? "Failed to reset password.";
            }
        }
        catch (Exception ex)
        {
            AdminResetErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsAdminResetSaving = false;
        }
    }

}

