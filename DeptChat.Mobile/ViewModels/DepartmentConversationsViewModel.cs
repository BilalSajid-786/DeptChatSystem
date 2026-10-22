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
}

[QueryProperty(nameof(DepartmentId), "deptId")]
[QueryProperty(nameof(DepartmentName), "deptName")]
public partial class DepartmentConversationsViewModel : ObservableObject
{
    private readonly ChatApiService? _apiService;
    private readonly AuthService? _authService;
    private readonly SignalRService? _signalRService;
    private List<ConversationListItem> _allConversations = new();

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

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool ShowSearchBar => _allConversations.Count > 0;

    public bool HasConversations => !IsEmpty && !IsSearchEmpty;

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

        if (_signalRService != null)
        {
            _signalRService.OnInAppToast += HandleInAppToast;
            _signalRService.OnMessageReceived += HandleMessageReceived;
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

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(ShowSearchBar));
    }

    partial void OnIsSearchEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasConversations));
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

    [RelayCommand]
    private void DismissToast()
    {
        ShowInAppToast = false;
    }

    private void HandleInAppToast(InAppNotificationToastDto toast)
    {
        // 1. If currently in this active chat room, ignore the toast banner
        if (ChatRoomViewModel.ActiveConversationId != Guid.Empty && toast.ConversationId == ChatRoomViewModel.ActiveConversationId)
        {
            return;
        }

        // 2. If this message is for the department currently displayed, refresh the list without an intrusive banner
        if (Guid.TryParse(DepartmentId, out var deptGuid) && toast.DepartmentId == deptGuid)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadConversationsAsync();
            });
            return;
        }

        // 3. For other departments, show toast banner with auto-dismiss
        MainThread.BeginInvokeOnMainThread(() =>
        {
            InAppToastMessage = $"🔔 {toast.DepartmentName}: {toast.SenderName}: {toast.ContentPreview}";
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
                    LastMessagePreview = string.IsNullOrWhiteSpace(c.LastMessagePreview) ? "No messages yet" : c.LastMessagePreview,
                    LastMessageAt = c.LastMessageAt,
                    UnreadCount = c.UnreadCount,
                    Status = c.Status
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

    [RelayCommand]
    private async Task SelectConversationAsync(ConversationListItem item)
    {
        if (item == null) return;

        await Shell.Current.GoToAsync($"ChatRoomPage?deptId={item.DepartmentId}&deptName={Uri.EscapeDataString(DepartmentName)}&convId={item.Id}&userName={Uri.EscapeDataString(item.CreatedByUserName)}");
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        await Shell.Current.GoToAsync("..");
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
            }
        }

        _authService?.Logout();
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
