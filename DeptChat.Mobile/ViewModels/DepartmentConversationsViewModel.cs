using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DeptChat.Mobile.Services;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;

namespace DeptChat.Mobile.ViewModels;

public class ConversationListItem : ObservableObject
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public string UserInitials => string.IsNullOrWhiteSpace(CreatedByUserName) ? "👤" : CreatedByUserName[..1].ToUpper();
    public string LastMessagePreview { get; set; } = "No messages yet";
    public DateTime? LastMessageAt { get; set; }
    public string TimeFormatted => LastMessageAt.HasValue
        ? (LastMessageAt.Value.Date == DateTime.UtcNow.Date ? LastMessageAt.Value.ToLocalTime().ToString("h:mm tt") : LastMessageAt.Value.ToLocalTime().ToString("MMM d, h:mm tt"))
        : "";
    public int UnreadCount { get; set; }
    public bool HasUnread => UnreadCount > 0;
    public ConversationStatus Status { get; set; }

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }

    public bool IsUserBlocked { get; set; }
}

public class AvailableUserItem : ObservableObject
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool HasExistingChat { get; set; }
    public string UserInitials => string.IsNullOrWhiteSpace(FullName) ? "👤" : FullName[..1].ToUpper();

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }

    public bool IsBlocked { get; set; }
}

[QueryProperty(nameof(DepartmentId), "deptId")]
[QueryProperty(nameof(DepartmentName), "deptName")]
public partial class DepartmentConversationsViewModel : ObservableObject
{
    private readonly ChatApiService? _apiService;
    private readonly AuthService? _authService;
    private readonly SignalRService? _signalRService;
    private List<ConversationListItem> _allConversations = new();
    private List<AvailableUserItem> _allAvailableUsers = new();

    [ObservableProperty]
    private string _departmentId = string.Empty;

    [ObservableProperty]
    private string _departmentName = "Department Chats";

    [ObservableProperty]
    private ObservableCollection<ConversationListItem> _conversations = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchEmpty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _showInAppToast;

    [ObservableProperty]
    private string _inAppToastMessage = string.Empty;

    // Admin & Manager WhatsApp FAB & Modal Properties
    [ObservableProperty]
    private bool _isAdminOrManager;

    [ObservableProperty]
    private bool _isSelectUserModalVisible = false;

    [ObservableProperty]
    private bool _isLoadingUsers = false;

    [ObservableProperty]
    private bool _showNoUsersFound = false;

    [ObservableProperty]
    private string _userSearchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AvailableUserItem> _filteredUsers = new();

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool ShowSearchBar => _allConversations.Count > 0;

    public bool HasConversations => !IsEmpty && !IsSearchEmpty;

    public bool ShowEmptyState => IsEmpty && !IsBusy && !IsSearchEmpty;

    public string SubtitleText
    {
        get
        {
            if (_allConversations.Count == 0) return "0 Active Chats";
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                return $"{Conversations.Count} of {_allConversations.Count} Found";
            }
            return $"{_allConversations.Count} Active {(_allConversations.Count == 1 ? "Chat" : "Chats")}";
        }
    }

    public DepartmentConversationsViewModel(
        ChatApiService apiService,
        AuthService authService,
        SignalRService signalRService)
    {
        _apiService = apiService;
        _authService = authService;
        _signalRService = signalRService;

        IsAdminOrManager = _authService?.Role == UserRole.Admin || _authService?.Role == UserRole.Manager;

        if (_signalRService != null)
        {
            _signalRService.OnInAppToast += HandleInAppToast;
            _signalRService.OnMessageReceived += HandleMessageReceived;
            _signalRService.OnUserOnlineStatusChanged += HandleUserOnlineStatusChanged;
            _signalRService.OnMessagesRead += (cId, seq) =>
            {
                MainThread.BeginInvokeOnMainThread(async () => await LoadConversationsAsync());
            };
        }
    }

    public DepartmentConversationsViewModel()
    {
    }

    partial void OnDepartmentIdChanged(string value)
    {
        _ = LoadConversationsAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchQuery));
        ApplyFilter();
    }

    partial void OnUserSearchQueryChanged(string value)
    {
        ApplyUserFilter();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(ShowSearchBar));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsSearchEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            Conversations = new ObservableCollection<ConversationListItem>(_allConversations);
            IsSearchEmpty = false;
        }
        else
        {
            var filtered = _allConversations.Where(c => 
                (!string.IsNullOrEmpty(c.CreatedByUserName) && c.CreatedByUserName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.LastMessagePreview) && c.LastMessagePreview.Contains(query, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            Conversations = new ObservableCollection<ConversationListItem>(filtered);
            IsSearchEmpty = filtered.Count == 0 && _allConversations.Count > 0;
        }

        IsEmpty = _allConversations.Count == 0;
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(ShowSearchBar));
    }

    private void ApplyUserFilter()
    {
        var query = UserSearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            FilteredUsers = new ObservableCollection<AvailableUserItem>(_allAvailableUsers);
        }
        else
        {
            var matched = _allAvailableUsers.Where(u =>
                (!string.IsNullOrEmpty(u.FullName) && u.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(u.Email) && u.Email.Contains(query, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            FilteredUsers = new ObservableCollection<AvailableUserItem>(matched);
        }

        ShowNoUsersFound = FilteredUsers.Count == 0 && !IsLoadingUsers;
    }

    [RelayCommand]
    private async Task OpenStartChatModalAsync()
    {
        UserSearchQuery = string.Empty;
        IsSelectUserModalVisible = true;
        await LoadAvailableUsersAsync();
    }

    [RelayCommand]
    private void CloseStartChatModal()
    {
        IsSelectUserModalVisible = false;
        UserSearchQuery = string.Empty;
    }

    public async Task LoadAvailableUsersAsync()
    {
        if (_apiService == null || !Guid.TryParse(DepartmentId, out var deptGuid)) return;

        try
        {
            IsLoadingUsers = true;
            ShowNoUsersFound = false;
            FilteredUsers.Clear();
            _allAvailableUsers.Clear();

            var dtos = await _apiService.GetAvailableUsersForDepartmentAsync(deptGuid);
            var items = dtos.Select(d => new AvailableUserItem
            {
                Id = d.Id,
                FullName = d.FullName,
                Email = d.Email,
                Role = d.Role,
                IsOnline = d.IsOnline,
                HasExistingChat = d.HasExistingChat,
                IsBlocked = d.IsBlocked
            }).ToList();

            _allAvailableUsers = items;
            ApplyUserFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DepartmentConversations] LoadAvailableUsers error: {ex.Message}");
            ShowNoUsersFound = true;
        }
        finally
        {
            IsLoadingUsers = false;
            ShowNoUsersFound = FilteredUsers.Count == 0;
        }
    }

    [RelayCommand]
    private async Task StartChatWithUserAsync(AvailableUserItem? user)
    {
        if (user == null || _apiService == null || !Guid.TryParse(DepartmentId, out var deptGuid)) return;

        try
        {
            IsBusy = true;
            IsSelectUserModalVisible = false;
            var conv = await _apiService.StartConversationWithUserAsync(deptGuid, user.Id);
            if (conv != null)
            {
                await Shell.Current.GoToAsync($"ChatRoomPage?deptId={conv.DepartmentId}&deptName={Uri.EscapeDataString(DepartmentName)}&convId={conv.Id}&userName={Uri.EscapeDataString(user.FullName)}&userId={user.Id}&isOnline={user.IsOnline}&isBlocked={user.IsBlocked}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DepartmentConversations] StartChatWithUser error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DismissToast()
    {
        ShowInAppToast = false;
    }

    private void HandleInAppToast(InAppNotificationToastDto toast)
    {
        if (ChatRoomViewModel.ActiveConversationId != Guid.Empty && toast.ConversationId == ChatRoomViewModel.ActiveConversationId)
        {
            return;
        }

        if (Guid.TryParse(DepartmentId, out var deptGuid) && toast.DepartmentId == deptGuid)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadConversationsAsync();
            });
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            InAppToastMessage = $"🔔 {toast.DepartmentName}: {toast.SenderName}: {SanitizePreview(toast.ContentPreview)}";
            ShowInAppToast = true;

            Task.Delay(4000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ShowInAppToast = false;
                });
            });
        });
    }

    private void HandleMessageReceived(ChatMessageDto msg)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await LoadConversationsAsync();
        });
    }

    [RelayCommand]
    public async Task LoadConversationsAsync()
    {
        if (_apiService == null || !Guid.TryParse(DepartmentId, out var deptGuid))
        {
            return;
        }

        try
        {
            IsBusy = true;
            var list = await _apiService.GetConversationsForDepartmentAsync(deptGuid);
            var items = new List<ConversationListItem>();

            foreach (var c in list)
            {
                items.Add(new ConversationListItem
                {
                    Id = c.Id,
                    DepartmentId = c.DepartmentId,
                    DepartmentName = c.DepartmentName,
                    CreatedByUserId = c.CreatedByUserId,
                    CreatedByUserName = string.IsNullOrWhiteSpace(c.CreatedByUserName) ? "Customer" : c.CreatedByUserName,
                    LastMessagePreview = SanitizePreview(c.LastMessagePreview),
                    LastMessageAt = c.LastMessageAt,
                    UnreadCount = c.UnreadCount,
                    Status = c.Status,
                    IsOnline = c.IsOnline,
                    IsUserBlocked = c.IsUserBlocked
                });
            }

            _allConversations = items;
            ApplyFilter();
        }
        catch
        {
            // Graceful fallback
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleUserOnlineStatusChanged(Guid userId, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = _allConversations.FirstOrDefault(c => c.CreatedByUserId == userId);
            if (item != null)
            {
                item.IsOnline = isOnline;
            }

            var modalUser = _allAvailableUsers.FirstOrDefault(u => u.Id == userId);
            if (modalUser != null)
            {
                modalUser.IsOnline = isOnline;
            }
        });
    }

    [RelayCommand]
    private async Task SelectConversationAsync(ConversationListItem item)
    {
        if (item == null) return;

        await Shell.Current.GoToAsync($"ChatRoomPage?deptId={item.DepartmentId}&deptName={Uri.EscapeDataString(DepartmentName)}&convId={item.Id}&userName={Uri.EscapeDataString(item.CreatedByUserName)}&userId={item.CreatedByUserId}&isOnline={item.IsOnline}&isBlocked={item.IsUserBlocked}");
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        await Shell.Current.GoToAsync("..");
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
            }
        }

        _authService?.Logout();
        await Shell.Current.GoToAsync("//LoginPage");
    }

    public static string SanitizePreview(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "No messages yet";
        if (raw.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase)) return "🎙️ Voice message";
        if (raw.StartsWith("[image:", StringComparison.OrdinalIgnoreCase))
        {
            var inner = raw.Trim('[', ']');
            var firstColon = inner.IndexOf(':');
            if (firstColon > 0)
            {
                var rest = inner.Substring(firstColon + 1);
                var secondColon = rest.IndexOf(':');
                if (secondColon > 0 && secondColon < rest.Length - 1)
                {
                    var caption = rest.Substring(secondColon + 1).Trim();
                    if (!string.IsNullOrEmpty(caption))
                        return $"📷 {caption}";
                }
            }
            return "📷 Photo";
        }
        if (raw.StartsWith("[file:", StringComparison.OrdinalIgnoreCase))
        {
            var inner = raw.Trim('[', ']');
            var parts = inner.Split(':');
            if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                return $"📄 {parts[2]}";
            }
            return "📎 Document";
        }
        return raw;
    }
}