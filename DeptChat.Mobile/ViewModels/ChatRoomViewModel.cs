using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DeptChat.Mobile.Services;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;

namespace DeptChat.Mobile.ViewModels;

public partial class ChatMessageItem : ObservableObject
{
    // Shared colour objects. These used to be created (and parsed from text) every time a row
    // was shown while scrolling; reusing them means less memory churn and fewer GC pauses.
    public static readonly Color OutgoingBubbleColor = Color.FromArgb("#E7FFDB");
    public static readonly Color PlayButtonOutgoingColor = Color.FromArgb("#059669");
    public static readonly Color PlayButtonIncomingColor = Color.FromArgb("#0284C7");
    public static readonly Color StatusSentColor = Color.FromArgb("#94A3B8");
    public static readonly Color StatusReadColor = Color.FromArgb("#34B7F1");

    public static Action<ChatMessageItem>? OnOpenImage;
    public static Action<ChatMessageItem>? OnOpenDocument;
    public static Action<ChatMessageItem>? OnTogglePlayVoice;

    [RelayCommand]
    private void OpenImage() => OnOpenImage?.Invoke(this);

    [RelayCommand]
    private void OpenDocument() => OnOpenDocument?.Invoke(this);

    [RelayCommand]
    private void TogglePlayVoice() => OnTogglePlayVoice?.Invoke(this);

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SenderId { get; set; }
    public UserRole SenderRole { get; set; } = UserRole.User;
    public string ClientGeneratedId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string TimeFormatted { get; set; } = DateTime.Now.ToString("h:mm tt");
    public DateTime MessageDate { get; set; } = DateTime.Now;

    internal bool _showDateHeader;
    public bool ShowDateHeader
    {
        get => _showDateHeader;
        set
        {
            if (SetProperty(ref _showDateHeader, value))
            {
                OnPropertyChanged(nameof(DateHeaderRowHeight));
                OnPropertyChanged(nameof(DateHeaderOpacity));
            }
        }
    }

    // Uses GridLength instead of IsVisible to avoid InvalidateMeasure on recycle
    public GridLength DateHeaderRowHeight => _showDateHeader ? GridLength.Auto : new GridLength(0);
    public double DateHeaderOpacity => _showDateHeader ? 1.0 : 0.0;

    internal string _dateHeaderText = string.Empty;
    public string DateHeaderText
    {
        get => _dateHeaderText;
        set => SetProperty(ref _dateHeaderText, value);
    }
    private bool _isOutgoing;
    public bool IsOutgoing
    {
        get => _isOutgoing;
        set
        {
            if (SetProperty(ref _isOutgoing, value))
            {
                _bubbleShape = null;
                OnPropertyChanged(nameof(Alignment));
                OnPropertyChanged(nameof(BubbleColor));
                OnPropertyChanged(nameof(BubbleShape));
                OnPropertyChanged(nameof(PlayButtonColor));
                OnPropertyChanged(nameof(ShowSenderName));
            }
        }
    }
    public long SequenceNumber { get; set; }

    private bool _isVoiceMessage;
    public bool IsVoiceMessage
    {
        get => _isVoiceMessage;
        set
        {
            if (SetProperty(ref _isVoiceMessage, value))
            {
                OnPropertyChanged(nameof(IsTextMessage));
                OnPropertyChanged(nameof(IsTextVisible));
                OnPropertyChanged(nameof(TextContent));
                OnPropertyChanged(nameof(IsVoiceVisible));
                OnPropertyChanged(nameof(IsMediaMessage));
            }
        }
    }
    public int VoiceDurationSeconds { get; set; } = 5;
    public string VoiceDurationText { get; set; } = "0:05";
    public string? LocalAudioPath { get; set; }
    public bool IsDragging { get; set; }

    // Media & File Attachments
    private bool _isImageMessage;
    public bool IsImageMessage
    {
        get => _isImageMessage;
        set
        {
            if (SetProperty(ref _isImageMessage, value))
            {
                OnPropertyChanged(nameof(IsTextMessage));
                OnPropertyChanged(nameof(IsTextVisible));
                OnPropertyChanged(nameof(TextContent));
                OnPropertyChanged(nameof(IsImageVisible));
                OnPropertyChanged(nameof(IsMediaMessage));
            }
        }
    }
    private string? _imageUrl;
    public string? ImageUrl
    {
        get => _imageUrl;
        set
        {
            if (SetProperty(ref _imageUrl, value)) OnPropertyChanged(nameof(ImageDisplaySource));
        }
    }

    // Picture shown in the chat list: a small copy made on the phone (see ChatImageThumbnails).
    // ImageUrl (full size) is still what opens when the picture is tapped.
    private ImageSource? _imageDisplaySource;
    private string? _imageDisplayFor;
    private bool _thumbnailRequested;

    public ImageSource? ImageDisplaySource
    {
        get
        {
            var url = _imageUrl;
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (_imageDisplayFor != url)
            {
                _imageDisplayFor = url;
                _imageDisplaySource = null;
                _thumbnailRequested = false;
            }

            if (_imageDisplaySource != null) return _imageDisplaySource;

            // Local file (e.g. a picture still being sent): show it directly, as before.
            if (!ChatImageThumbnails.IsRemote(url))
            {
                _imageDisplaySource = ImageSource.FromFile(url);
                return _imageDisplaySource;
            }

            var cached = ChatImageThumbnails.TryGetCached(url);
            if (cached != null)
            {
                _imageDisplaySource = ImageSource.FromFile(cached);
                return _imageDisplaySource;
            }

            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync(url);
            }
            return null; // dark placeholder until the small copy is ready
        }
    }

    private async Task LoadThumbnailAsync(string url)
    {
        var path = await ChatImageThumbnails.GetOrCreateAsync(url).ConfigureAwait(false);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_imageDisplayFor != url) return;
            // Fall back to the original picture (previous behaviour) if shrinking failed.
            _imageDisplaySource = path != null ? ImageSource.FromFile(path) : ImageSource.FromUri(new Uri(url));
            OnPropertyChanged(nameof(ImageDisplaySource));
        });
    }
    public string? ImageCaption { get; set; }
    public bool HasCaption => !string.IsNullOrWhiteSpace(ImageCaption);

    private bool _isDocumentMessage;
    public bool IsDocumentMessage
    {
        get => _isDocumentMessage;
        set
        {
            if (SetProperty(ref _isDocumentMessage, value))
            {
                OnPropertyChanged(nameof(IsTextMessage));
                OnPropertyChanged(nameof(IsTextVisible));
                OnPropertyChanged(nameof(TextContent));
                OnPropertyChanged(nameof(IsDocumentVisible));
                OnPropertyChanged(nameof(IsMediaMessage));
            }
        }
    }
    public string? DocumentUrl { get; set; }
    public string? DocumentFileName { get; set; }
    public string? DocumentFileSizeText { get; set; }

    private bool _isDeleted;
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (SetProperty(ref _isDeleted, value))
            {
                OnPropertyChanged(nameof(IsTextMessage));
                OnPropertyChanged(nameof(IsMediaMessage));
                OnPropertyChanged(nameof(IsStatusVisible));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(IsTextVisible));
                OnPropertyChanged(nameof(TextContent));
                OnPropertyChanged(nameof(IsImageVisible));
                OnPropertyChanged(nameof(IsDocumentVisible));
                OnPropertyChanged(nameof(IsVoiceVisible));
                OnPropertyChanged(nameof(IsForwardedVisible));
            }
        }
    }

    private bool _isForwarded;
    public bool IsForwarded
    {
        get => _isForwarded;
        set
        {
            if (SetProperty(ref _isForwarded, value))
            {
                OnPropertyChanged(nameof(IsForwardedVisible));
            }
        }
    }

    public string DisplayText => IsDeleted ? "🚫 This message was deleted" : Content;
    public bool IsMediaMessage => !IsDeleted && (IsImageMessage || IsVoiceMessage || IsDocumentMessage);
    public bool IsStatusVisible => !IsDeleted && IsOutgoing;
    public bool IsTextMessage => !IsVoiceMessage && !IsImageMessage && !IsDocumentMessage;

    public bool IsTextVisible => !IsDeleted && IsTextMessage;

    /// <summary>
    /// Text for the message label: the content only for real text messages. Voice notes keep their
    /// (very large) base64 audio in Content; giving that to a hidden label made scrolling stutter.
    /// </summary>
    public string TextContent => IsTextVisible ? Content : string.Empty;
    public bool IsImageVisible => !IsDeleted && IsImageMessage;
    public bool IsDocumentVisible => !IsDeleted && IsDocumentMessage;
    public bool IsVoiceVisible => !IsDeleted && IsVoiceMessage;
    public bool IsForwardedVisible => !IsDeleted && IsForwarded;

    private double _playbackProgress;
    public double PlaybackProgress
    {
        get => _playbackProgress;
        set => SetProperty(ref _playbackProgress, value);
    }

    private string _currentPlaybackTime = string.Empty;
    public string CurrentPlaybackTime
    {
        get => string.IsNullOrEmpty(_currentPlaybackTime) ? VoiceDurationText : _currentPlaybackTime;
        set => SetProperty(ref _currentPlaybackTime, value);
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            SetProperty(ref _isPlaying, value);
            OnPropertyChanged(nameof(PlayButtonIcon));
        }
    }

    public string PlayButtonIcon => IsPlaying ? "⏸" : "▶";
    public Color PlayButtonColor => IsOutgoing ? PlayButtonOutgoingColor : PlayButtonIncomingColor;

    public MessageDeliveryStatus Status { get; set; } = MessageDeliveryStatus.Pending;

    private string _statusIcon = "🕒";
    public string StatusIcon
    {
        get => _statusIcon;
        set => SetProperty(ref _statusIcon, value);
    }

    private Color _statusColor = StatusSentColor;
    public Color StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    public LayoutOptions Alignment => IsOutgoing ? LayoutOptions.End : LayoutOptions.Start;
    public Color BubbleColor => IsOutgoing ? OutgoingBubbleColor : Colors.White;

    private Microsoft.Maui.Graphics.IShape? _bubbleShape;
    public Microsoft.Maui.Graphics.IShape BubbleShape =>
        _bubbleShape ??= new Microsoft.Maui.Controls.Shapes.RoundRectangle
        {
            CornerRadius = IsOutgoing
                ? new CornerRadius(16, 16, 6, 16)
                : new CornerRadius(16, 16, 16, 6)
        };

    private bool _showSenderName;
    public bool ShowSenderName
    {
        get => _showSenderName;
        set => SetProperty(ref _showSenderName, value);
    }
}

public class ForwardDepartmentItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
}

public class ForwardUserItem
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public string AvatarInitial => !string.IsNullOrWhiteSpace(FullName) ? FullName[..1].ToUpperInvariant() : "👤";
    public string OnlineBadgeText => IsOnline ? "ONLINE" : "";
    public bool ShowOnlineBadge => IsOnline;
}

[QueryProperty(nameof(DepartmentId), "deptId")]
[QueryProperty(nameof(DepartmentName), "deptName")]
[QueryProperty(nameof(ConversationIdString), "convId")]
[QueryProperty(nameof(CustomerName), "userName")]
[QueryProperty(nameof(OtherUserIdString), "userId")]
[QueryProperty(nameof(InitialIsOnlineString), "isOnline")]
[QueryProperty(nameof(InitialIsBlockedString), "isBlocked")]
[QueryProperty(nameof(SharedFilePath), "sharedFilePath")]
public partial class ChatRoomViewModel : ObservableObject, IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("deptId", out var deptIdObj) && deptIdObj != null)
            DepartmentId = Uri.UnescapeDataString(deptIdObj.ToString() ?? string.Empty);

        if (query.TryGetValue("deptName", out var deptNameObj) && deptNameObj != null)
            DepartmentName = Uri.UnescapeDataString(deptNameObj.ToString() ?? string.Empty);

        if (query.TryGetValue("convId", out var convIdObj) && convIdObj != null)
        {
            var cStr = convIdObj.ToString() ?? string.Empty;
            ConversationIdString = cStr;
            if (Guid.TryParse(cStr, out var cGuid) && cGuid != Guid.Empty)
            {
                _conversationId = cGuid;
                _convIdTcs.TrySetResult(_conversationId);
                ActiveConversationId = _conversationId;
            }
        }

        if (query.TryGetValue("userName", out var userNameObj) && userNameObj != null)
            CustomerName = Uri.UnescapeDataString(userNameObj.ToString() ?? string.Empty);

        if (query.TryGetValue("userId", out var userIdObj) && userIdObj != null)
            OtherUserIdString = userIdObj.ToString() ?? string.Empty;

        if (query.TryGetValue("isOnline", out var isOnlineObj) && isOnlineObj != null)
            InitialIsOnlineString = isOnlineObj.ToString() ?? string.Empty;

        if (query.TryGetValue("isBlocked", out var isBlockedObj) && isBlockedObj != null)
            InitialIsBlockedString = isBlockedObj.ToString() ?? string.Empty;

        if (query.TryGetValue("sharedFilePath", out var shareObj) && shareObj != null)
            SharedFilePath = Uri.UnescapeDataString(shareObj.ToString() ?? string.Empty);
    }

    [ObservableProperty]
    private string _sharedFilePath = string.Empty;

    [ObservableProperty]
    private bool _hasPendingAttachment;

    [ObservableProperty]
    private string _pendingAttachmentPath = string.Empty;

    [ObservableProperty]
    private string _pendingAttachmentTitle = string.Empty;

    [ObservableProperty]
    private string _pendingAttachmentSubtitle = string.Empty;

    [ObservableProperty]
    private ImageSource? _pendingAttachmentPreview;

    partial void OnSharedFilePathChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var unescaped = Uri.UnescapeDataString(value);
            SetPendingAttachment(unescaped);
        }
    }

    [RelayCommand]
    public void CancelPendingAttachment()
    {
        ClearPendingAttachment();
    }

    public void ClearPendingAttachment()
    {
        HasPendingAttachment = false;
        PendingAttachmentPath = string.Empty;
        PendingAttachmentTitle = string.Empty;
        PendingAttachmentSubtitle = string.Empty;
        PendingAttachmentPreview = null;
        IncomingShareService.Instance.ClearPendingShare();
    }

    public void SetPendingAttachment(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        PendingAttachmentPath = filePath;
        var fileName = Path.GetFileName(filePath);
        PendingAttachmentTitle = fileName.StartsWith("shared_receipt_") ? "Transaction Receipt" : fileName;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".pdf")
        {
            PendingAttachmentSubtitle = "PDF Document • Ready to send";
            PendingAttachmentPreview = null;
        }
        else
        {
            PendingAttachmentSubtitle = "Image Receipt • Ready to send";
            try
            {
                PendingAttachmentPreview = ImageSource.FromFile(filePath);
            }
            catch { }
        }

        HasPendingAttachment = true;
    }

    private readonly SignalRService? _signalRService;
    private readonly ChatApiService? _apiService;
    private readonly AuthService? _authService;
    private readonly LocalOutboxService? _outboxService;
    private readonly IAudioService? _audioService;

    public static Guid ActiveConversationId { get; set; } = Guid.Empty;
    private CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _outgoingSendLock = new(1, 1);
    private bool _isSubscribed;
    private System.Timers.Timer? _playbackTimer;
    private double _playbackElapsedSeconds;
    private bool _isPreviewDragging;

    private Guid _conversationId = Guid.Empty;
    private readonly TaskCompletionSource<Guid> _convIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _lastSequenceNumber = 0;

    public static long GetHighestContiguousSequence(IEnumerable<long> sequenceNumbers)
    {
        var sorted = sequenceNumbers.Where(s => s > 0).Distinct().OrderBy(s => s).ToList();
        if (sorted.Count == 0) return 0;

        long expected = 1;
        long lastContiguous = 0;
        foreach (var seq in sorted)
        {
            if (seq == expected)
            {
                lastContiguous = seq;
                expected++;
            }
            else
            {
                break;
            }
        }
        return lastContiguous;
    }

    public void UpdateDateHeaders()
    {
        DateTime? previousDate = null;
        for (int i = 0; i < Messages.Count; i++)
        {
            var msg = Messages[i];
            var msgDate = msg.MessageDate.Date;
            bool shouldShow = !previousDate.HasValue || previousDate.Value != msgDate;
            if (shouldShow)
            {
                var formatted = FormatDateHeader(msgDate);
                // Only fire PropertyChanged if value actually changed
                if (msg.DateHeaderText != formatted) msg.DateHeaderText = formatted;
                if (!msg.ShowDateHeader) msg.ShowDateHeader = true;
                previousDate = msgDate;
            }
            else
            {
                if (msg.ShowDateHeader) msg.ShowDateHeader = false;
            }
        }
    }

    /// <summary>
    /// Pre-computes ShowDateHeader/DateHeaderText on items BEFORE they are inserted
    /// into the ObservableCollection, so no PropertyChanged fires during scroll.
    /// Call this on a list of items that will be inserted at position 0.
    /// </summary>
    public void PreComputeDateHeaders(IReadOnlyList<ChatMessageItem> items)
    {
        DateTime? previousDate = null;
        for (int i = 0; i < items.Count; i++)
        {
            var msg = items[i];
            var msgDate = msg.MessageDate.Date;
            if (!previousDate.HasValue || previousDate.Value != msgDate)
            {
                msg._showDateHeader = true;
                msg._dateHeaderText = FormatDateHeader(msgDate);
                previousDate = msgDate;
            }
            else
            {
                msg._showDateHeader = false;
                msg._dateHeaderText = string.Empty;
            }
        }
    }

    public static string FormatDateHeader(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today)
            return "TODAY";
        if (date == today.AddDays(-1))
            return "YESTERDAY";
        return date.ToString("dddd, d MMMM yyyy");
    }

    private void SortMessages()
    {
        var isSorted = true;
        for (int i = 0; i < Messages.Count - 1; i++)
        {
            var a = Messages[i];
            var b = Messages[i + 1];
            var aKey = a.SequenceNumber > 0 ? a.SequenceNumber : long.MaxValue;
            var bKey = b.SequenceNumber > 0 ? b.SequenceNumber : long.MaxValue;
            if (aKey > bKey)
            {
                isSorted = false;
                break;
            }
        }
        if (isSorted)
        {
            UpdateDateHeaders();
            return;
        }

        var sorted = Messages.OrderBy(m => m.SequenceNumber > 0 ? m.SequenceNumber : long.MaxValue).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var currentIndex = Messages.IndexOf(sorted[i]);
            if (currentIndex != i && currentIndex >= 0)
            {
                Messages.Move(currentIndex, i);
            }
        }
        UpdateDateHeaders();
    }

    private void ReorderMessageIfNeeded(ChatMessageItem item)
    {
        SortMessages();
    }

    public async Task SyncMissedMessagesAsync()
    {
        if (_apiService == null) return;
        var convId = await EnsureConversationIdAsync();
        if (convId == Guid.Empty) return;

        await _syncLock.WaitAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Messages.Count > 0)
            {
                IsSyncingMessages = true;
            }
        });
        try
        {
            long syncFromSeq;
            lock (Messages)
            {
                syncFromSeq = GetHighestContiguousSequence(Messages.Select(m => m.SequenceNumber));
            }

            if (syncFromSeq == 0)
            {
                await LoadInitialMessagesAsync();
                return;
            }

            var sync = await _apiService.SyncDeltaAsync(convId, syncFromSeq);
            if (sync != null)
            {
                // If server reports 0 messages, but local device has confirmed server messages, clear only confirmed messages (protect pending outbox messages!)
                if (sync.LastSequenceNumber == 0 && sync.Messages.Count == 0 && Messages.Any(m => m.SequenceNumber > 0))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var confirmed = Messages.Where(m => m.SequenceNumber > 0).ToList();
                        foreach (var cm in confirmed)
                        {
                            Messages.Remove(cm);
                        }
                    });
                    _lastSequenceNumber = 0;
                    if (_outboxService != null)
                    {
                        _ = _outboxService.ClearConfirmedMessagesAsync(convId);
                    }
                }
                else if (sync.Messages.Count > 0)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        foreach (var m in sync.Messages)
                        {
                            AddOrUpdateServerMessage(m, reorder: false, scroll: false);
                            if (_outboxService != null)
                            {
                                _ = _outboxService.SaveServerMessageAsync(
                                    convId,
                                    m.Id,
                                    m.SenderId,
                                    m.SenderRole,
                                    m.ClientGeneratedId,
                                    m.Content,
                                    m.SenderName,
                                    m.SenderId == (_authService?.UserId ?? Guid.Empty),
                                    m.SequenceNumber,
                                    m.Status,
                                    m.SentAt,
                                    m.IsDeleted,
                                    m.IsForwarded
                                );
                            }
                        }
                        SortMessages();
                    });

                    if (sync.LastSequenceNumber > _lastSequenceNumber)
                    {
                        _lastSequenceNumber = sync.LastSequenceNumber;
                    }

                    var myUserId = _authService?.UserId ?? Guid.Empty;
                    var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
                    var isStaffViewer = myRole == UserRole.Admin || myRole == UserRole.Manager;
                    var unreadIncoming = isStaffViewer
                        ? sync.Messages.Where(m => m.SenderRole == UserRole.User && m.SenderId != myUserId).ToList()
                        : sync.Messages.Where(m => m.SenderRole != UserRole.User && m.SenderId != myUserId).ToList();

                    if (unreadIncoming.Count > 0 && _signalRService != null && _signalRService.IsConnected)
                    {
                        var maxIncomingSeq = unreadIncoming.Max(m => m.SequenceNumber);
                        _ = _signalRService.MarkAsReadAsync(convId, maxIncomingSeq);
                    }
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Conversation was wiped/deleted on server
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Messages.Clear();
            });
            _lastSequenceNumber = 0;
            if (_outboxService != null)
            {
                _ = _outboxService.ClearConversationMessagesAsync(convId);
            }
            if (Guid.TryParse(DepartmentId, out var deptG))
            {
                var myUId = _authService?.UserId ?? Guid.Empty;
                Preferences.Default.Remove($"dept_conv_{deptG}_{myUId}");
            }
        }
        catch
        {
            // Network or transient sync error
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsSyncingMessages = false;
            });
            _syncLock.Release();
        }
    }

    private async Task LoadInitialMessagesAsync()
    {
        if (_apiService == null) return;
        var convId = await EnsureConversationIdAsync();
        if (convId == Guid.Empty) return;

        try
        {
            var paged = await _apiService.GetMessagesAsync(convId, beforeSequenceNumber: null, pageSize: 50);
            if (paged != null)
            {
                HasMoreOlderMessages = paged.HasMore;
                if (paged.EarliestSequenceNumber.HasValue)
                {
                    _earliestLoadedSequenceNumber = paged.EarliestSequenceNumber.Value;
                }

                if (paged.Messages.Count > 0)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        foreach (var m in paged.Messages)
                        {
                            AddOrUpdateServerMessage(m, reorder: false, scroll: false);
                            if (_outboxService != null)
                            {
                                _ = _outboxService.SaveServerMessageAsync(
                                    convId,
                                    m.Id,
                                    m.SenderId,
                                    m.SenderRole,
                                    m.ClientGeneratedId,
                                    m.Content,
                                    m.SenderName,
                                    m.SenderId == (_authService?.UserId ?? Guid.Empty),
                                    m.SequenceNumber,
                                    m.Status,
                                    m.SentAt,
                                    m.IsDeleted,
                                    m.IsForwarded
                                );
                            }
                        }
                        SortMessages();
                        TriggerScrollToBottom();
                    });

                    if (paged.LatestSequenceNumber.HasValue && paged.LatestSequenceNumber.Value > _lastSequenceNumber)
                    {
                        _lastSequenceNumber = paged.LatestSequenceNumber.Value;
                    }

                    var myUserId = _authService?.UserId ?? Guid.Empty;
                    var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
                    var isStaffViewer = myRole == UserRole.Admin || myRole == UserRole.Manager;
                    var unreadIncoming = isStaffViewer
                        ? paged.Messages.Where(m => m.SenderRole == UserRole.User && m.SenderId != myUserId).ToList()
                        : paged.Messages.Where(m => m.SenderRole != UserRole.User && m.SenderId != myUserId).ToList();

                    if (unreadIncoming.Count > 0 && _signalRService != null && _signalRService.IsConnected)
                    {
                        var maxIncomingSeq = unreadIncoming.Max(m => m.SequenceNumber);
                        _ = _signalRService.MarkAsReadAsync(convId, maxIncomingSeq);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoom] LoadInitialMessages error: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task LoadOlderMessagesAsync()
    {
        if (IsLoadingOlderMessages || !HasMoreOlderMessages || _apiService == null || _conversationId == Guid.Empty)
            return;

        if (_earliestLoadedSequenceNumber <= 1)
        {
            HasMoreOlderMessages = false;
            return;
        }

        IsLoadingOlderMessages = true;
        try
        {
            // Downloaded and parsed off the UI thread (older pages can be large because of voice notes).
            var paged = await _apiService.GetMessagesInBackgroundAsync(_conversationId, beforeSequenceNumber: _earliestLoadedSequenceNumber, pageSize: 25);
            if (paged != null)
            {
                HasMoreOlderMessages = paged.HasMore;
                if (paged.EarliestSequenceNumber.HasValue)
                {
                    _earliestLoadedSequenceNumber = Math.Min(_earliestLoadedSequenceNumber, paged.EarliestSequenceNumber.Value);
                }
                else
                {
                    HasMoreOlderMessages = false;
                }

                if (paged.Messages.Count > 0)
                {
                    // Save to SQLite outbox cache on background thread to prevent UI thread stutter
                    if (_outboxService != null)
                    {
                        var convId = _conversationId;
                        var myUserId = _authService?.UserId ?? Guid.Empty;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                foreach (var m in paged.Messages)
                                {
                                    await _outboxService.SaveServerMessageAsync(
                                        convId,
                                        m.Id,
                                        m.SenderId,
                                        m.SenderRole,
                                        m.ClientGeneratedId,
                                        m.Content,
                                        m.SenderName,
                                        m.SenderId == myUserId,
                                        m.SequenceNumber,
                                        m.Status,
                                        m.SentAt,
                                        m.IsDeleted,
                                        m.IsForwarded
                                    );
                                }
                            }
                            catch { }
                        });
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var sortedOlder = paged.Messages.OrderBy(m => m.SequenceNumber).ToList();
                        var newItems = new List<ChatMessageItem>();

                        foreach (var m in sortedOlder)
                        {
                            var existing = Messages.FirstOrDefault(x => x.ClientGeneratedId == m.ClientGeneratedId || x.Id == m.Id);
                            if (existing != null)
                            {
                                existing.Id = m.Id;
                                existing.SequenceNumber = m.SequenceNumber;
                                existing.IsDeleted = m.IsDeleted;
                                existing.IsForwarded = m.IsForwarded;
                                ApplyRoleAwareAlignment(existing, m.SenderId, m.SenderRole, m.SenderName);
                                UpdateStatusVisuals(existing, m.Status);
                            }
                            else
                            {
                                var newItem = new ChatMessageItem
                                {
                                    Id = m.Id,
                                    ClientGeneratedId = m.ClientGeneratedId,
                                    Content = m.Content,
                                    TimeFormatted = m.SentAt.ToLocalTime().ToString("h:mm tt"),
                                    MessageDate = m.SentAt.ToLocalTime(),
                                    SequenceNumber = m.SequenceNumber,
                                    IsDeleted = m.IsDeleted,
                                    IsForwarded = m.IsForwarded
                                };
                                ApplyRoleAwareAlignment(newItem, m.SenderId, m.SenderRole, m.SenderName);
                                ParseMessagePayload(newItem, m.Content);
                                UpdateStatusVisuals(newItem, m.Status);

                                newItems.Add(newItem);
                            }
                        }

                        if (newItems.Count > 0)
                        {
                            // Precompute date headers on the new older batch
                            PreComputeDateHeaders(newItems);

                            // Also adjust boundary: if the last item of newItems has the same date
                            // as what used to be the first item of Messages, hide header on first existing
                            var firstExisting = Messages.FirstOrDefault();
                            if (firstExisting != null && newItems[^1].MessageDate.Date == firstExisting.MessageDate.Date)
                            {
                                firstExisting.ShowDateHeader = false;
                            }

                            PreparingOlderMessagesInsert?.Invoke();

                            for (int i = 0; i < newItems.Count; i++)
                            {
                                Messages.Insert(i, newItems[i]);
                            }

                            OlderMessagesInserted?.Invoke(newItems.Count);
                        }
                    });
                }
            }
            else
            {
                HasMoreOlderMessages = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoom] LoadOlderMessages error: {ex.Message}");
        }
        finally
        {
            IsLoadingOlderMessages = false;
        }
    }

    private async Task<Guid> EnsureConversationIdAsync()
    {
        if (_conversationId != Guid.Empty)
            return _conversationId;

        var completed = await Task.WhenAny(_convIdTcs.Task, Task.Delay(3000));
        if (completed == _convIdTcs.Task)
        {
            return await _convIdTcs.Task;
        }

        if (IsCustomer && Guid.TryParse(DepartmentId, out var deptGuid) && _apiService != null)
        {
            try
            {
                var convs = await _apiService.GetConversationsForDepartmentAsync(deptGuid);
                var conv = convs.FirstOrDefault();
                if (conv != null)
                {
                    _conversationId = conv.Id;
                    _convIdTcs.TrySetResult(_conversationId);
                    ActiveConversationId = _conversationId;
                    var myUserId = _authService?.UserId ?? Guid.Empty;
                    Preferences.Default.Set($"dept_conv_{deptGuid}_{myUserId}", _conversationId.ToString());
                    return _conversationId;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatRoom] EnsureConversationIdAsync error: {ex.Message}");
            }
        }

        return _conversationId;
    }

    [ObservableProperty]
    private string _departmentId = string.Empty;

    [ObservableProperty]
    private string _departmentName = "Department Chat";

    [ObservableProperty]
    private string _conversationIdString = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _otherUserIdString = string.Empty;

    partial void OnOtherUserIdStringChanged(string value)
    {
        if (Guid.TryParse(value, out var uid))
        {
            OtherUserId = uid;
        }
    }

    [ObservableProperty]
    private string _initialIsOnlineString = string.Empty;

    partial void OnInitialIsOnlineStringChanged(string value)
    {
        if (bool.TryParse(value, out var isOnline))
        {
            IsOtherUserOnline = isOnline;
            if (isOnline)
            {
                PresenceStatusText = "online";
                PresenceTextColor = Color.FromArgb("#A7F3D0");
            }
            else if (IsCustomer)
            {
                PresenceStatusText = string.Empty;
            }
            else
            {
                PresenceStatusText = FormatLastSeen(DateTime.UtcNow);
                PresenceTextColor = Color.FromArgb("#D8E2DC");
            }
        }
    }

    public string ContactTitle
    {
        get
        {
            var isCustomer = _authService?.Role == UserRole.User || _authService?.CurrentUser?.Role == UserRole.User;
            if (isCustomer)
            {
                return DepartmentName;
            }
            return !string.IsNullOrWhiteSpace(CustomerName) ? CustomerName : DepartmentName;
        }
    }
    public string AvatarInitial => !string.IsNullOrWhiteSpace(ContactTitle) ? ContactTitle.Substring(0, 1).ToUpper() : "D";

    public string DisplayTitle
    {
        get
        {
            var isCustomer = _authService?.Role == UserRole.User || _authService?.CurrentUser?.Role == UserRole.User;
            if (isCustomer)
            {
                return DepartmentName;
            }
            return !string.IsNullOrWhiteSpace(CustomerName)
                ? $"{CustomerName} • {DepartmentName}"
                : DepartmentName;
        }
    }

    partial void OnDepartmentNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ContactTitle));
        OnPropertyChanged(nameof(AvatarInitial));
    }

    partial void OnCustomerNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ContactTitle));
        OnPropertyChanged(nameof(AvatarInitial));
    }

    [ObservableProperty]
    private string _presenceStatusText = string.Empty;

    public bool HasPresenceStatus => !string.IsNullOrWhiteSpace(PresenceStatusText);

    partial void OnPresenceStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPresenceStatus));
    }

    [ObservableProperty]
    private Color _presenceTextColor = Color.FromArgb("#A7F3D0");

    [ObservableProperty]
    private bool _isOtherUserOnline = true;

    [ObservableProperty]
    private bool _isOtherUserTyping;

    public bool IsCustomer => _authService?.Role == UserRole.User || _authService?.CurrentUser?.Role == UserRole.User;
    public bool IsStaffViewer => !IsCustomer;
    public bool IsAdmin => _authService?.Role == UserRole.Admin || _authService?.CurrentUser?.Role == UserRole.Admin;

    public Guid OtherUserId { get; set; } = Guid.Empty;

    [ObservableProperty]
    private string _initialIsBlockedString = string.Empty;

    partial void OnInitialIsBlockedStringChanged(string value)
    {
        if (bool.TryParse(value, out var b))
        {
            IsTargetUserBlocked = b;
        }
    }

    [ObservableProperty]
    private bool _isTargetUserBlocked;

    [ObservableProperty]
    private bool _isCurrentUserBlocked;

    [ObservableProperty]
    private bool _isMessageMenuVisible;

    [ObservableProperty]
    private ChatMessageItem? _selectedMenuMessage;

    [ObservableProperty]
    private string _selectedMessagePreviewText = string.Empty;

    [ObservableProperty]
    private string _selectedMessageSenderName = string.Empty;

    [ObservableProperty]
    private string _selectedMessageTime = string.Empty;

    [ObservableProperty]
    private bool _canCopySelectedMessage;

    [ObservableProperty]
    private bool _canForwardSelectedMessage;

    [ObservableProperty]
    private bool _canDeleteSelectedMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForwardStep0))]
    [NotifyPropertyChangedFor(nameof(IsForwardStep1))]
    private int _forwardStep = 0; // 0 = Dept, 1 = User

    public bool IsForwardStep0 => ForwardStep == 0;
    public bool IsForwardStep1 => ForwardStep == 1;

    [ObservableProperty]
    private bool _isForwardModalVisible;

    [ObservableProperty]
    private ChatMessageItem? _messageToForward;

    [ObservableProperty]
    private ForwardDepartmentItem? _selectedForwardDepartment;

    [ObservableProperty]
    private ObservableCollection<ForwardDepartmentItem> _forwardDepartments = new();

    [ObservableProperty]
    private ObservableCollection<ForwardDepartmentItem> _filteredForwardDepartments = new();

    [ObservableProperty]
    private string _forwardDeptSearchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ForwardUserItem> _departmentForwardUsers = new();

    [ObservableProperty]
    private ObservableCollection<ForwardUserItem> _filteredDepartmentForwardUsers = new();

    [ObservableProperty]
    private string _forwardUserSearchText = string.Empty;

    [ObservableProperty]
    private bool _isLoadingForwardData;

    [ObservableProperty]
    private string _inputMessage = string.Empty;

    public bool HasInputText => !string.IsNullOrWhiteSpace(InputMessage);

    partial void OnInputMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasInputText));
        HandleTypingDebounce(value);
    }

    private System.Timers.Timer? _typingTimer;
    private bool _isCurrentlyTypingSent;

    private void HandleTypingDebounce(string text)
    {
        if (_signalRService == null || _conversationId == Guid.Empty) return;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (_isCurrentlyTypingSent)
            {
                _isCurrentlyTypingSent = false;
                _ = _signalRService.SendTypingAsync(_conversationId, false);
            }
            _typingTimer?.Stop();
            return;
        }

        if (!_isCurrentlyTypingSent)
        {
            _isCurrentlyTypingSent = true;
            _ = _signalRService.SendTypingAsync(_conversationId, true);
        }

        _typingTimer?.Stop();
        _typingTimer?.Dispose();
        _typingTimer = new System.Timers.Timer(2500);
        _typingTimer.AutoReset = false;
        _typingTimer.Elapsed += (s, e) =>
        {
            _isCurrentlyTypingSent = false;
            if (_conversationId != Guid.Empty && _signalRService != null)
            {
                _ = _signalRService.SendTypingAsync(_conversationId, false);
            }
        };
        _typingTimer.Start();
    }

    [ObservableProperty]
    private ObservableCollection<ChatMessageItem> _messages = new();

    public event Action? RequestScrollToBottom;
    public event Action<ChatMessageItem>? RequestScrollToItem;
    public event Action? PreparingOlderMessagesInsert;
    public event Action<int>? OlderMessagesInserted;

    public void TriggerScrollToBottom() => RequestScrollToBottom?.Invoke();

    // Voice message recording & preview states
    [ObservableProperty]
    private bool _isRecordingVoice;

    [ObservableProperty]
    private string _recordingDurationText = "0:00";

    [ObservableProperty]
    private bool _isVoicePreviewReady;

    [ObservableProperty]
    private bool _isVoicePreviewPlaying;

    [ObservableProperty]
    private double _voicePreviewProgress;

    [ObservableProperty]
    private string _voicePreviewTimeText = "0:00";

    public string VoicePreviewPlayIcon => IsVoicePreviewPlaying ? "⏸" : "▶";

    private System.Timers.Timer? _recordingTimer;
    private System.Timers.Timer? _previewPlaybackTimer;
    private double _previewElapsedSeconds;
    private int _recordingSeconds;
    private string? _activeRecordingPath;
    private ChatMessageItem? _currentlyPlayingItem;

    // In-App Toast Banner properties (For cross-department alerts)
    [ObservableProperty]
    private bool _showInAppToast;

    [ObservableProperty]
    private string _inAppToastMessage = string.Empty;

    // Network loading and sync feedback
    [ObservableProperty]
    private bool _isLoadingMessages;

    [ObservableProperty]
    private bool _isSyncingMessages;

    [ObservableProperty]
    private bool _isLoadingOlderMessages;

    [ObservableProperty]
    private bool _hasMoreOlderMessages = false;

    private long _earliestLoadedSequenceNumber = long.MaxValue;

    [ObservableProperty]
    private string _loadingStatusText = "Loading messages...";

    public ChatRoomViewModel(
        SignalRService signalRService,
        ChatApiService apiService,
        AuthService authService,
        LocalOutboxService outboxService,
        IAudioService audioService)
    {
        _signalRService = signalRService;
        _apiService = apiService;
        _authService = authService;
        _outboxService = outboxService;
        _audioService = audioService;

        ChatMessageItem.OnOpenImage = item => _ = OpenImageAsync(item);
        ChatMessageItem.OnOpenDocument = item => _ = OpenDocumentAsync(item);
        ChatMessageItem.OnTogglePlayVoice = item => _ = TogglePlayVoiceAsync(item);

        SubscribeToEvents();

        // Check if an external receipt/image was shared into DeptChat
        if (!string.IsNullOrEmpty(SharedFilePath))
        {
            SetPendingAttachment(Uri.UnescapeDataString(SharedFilePath));
        }
        else if (IncomingShareService.Instance.HasPendingShare)
        {
            SetPendingAttachment(IncomingShareService.Instance.PendingSharedFilePath!);
        }
    }

    public ChatRoomViewModel()
    {
        ChatMessageItem.OnOpenImage = item => _ = OpenImageAsync(item);
        ChatMessageItem.OnOpenDocument = item => _ = OpenDocumentAsync(item);
        ChatMessageItem.OnTogglePlayVoice = item => _ = TogglePlayVoiceAsync(item);
    }

    public void SubscribeToEvents()
    {
        if (_isSubscribed) return;

        if (_signalRService != null)
        {
            _signalRService.OnMessageReceived += HandleMessageReceived;
            _signalRService.OnMessageAck += HandleMessageAck;
            _signalRService.OnMessageDelivered += HandleMessageDelivered;
            _signalRService.OnMessagesRead += HandleMessagesRead;
            _signalRService.OnMessageDeleted += HandleMessageDeleted;
            _signalRService.OnInAppToast += HandleInAppToast;
            _signalRService.OnReconnected += HandleSignalRReconnected;
            _signalRService.OnUserPresenceChanged += HandleUserPresenceChanged;
            _signalRService.OnUserTyping += HandleUserTyping;
            _signalRService.OnUserPresenceState += HandleUserPresenceState;
            _signalRService.OnDepartmentPresenceState += HandleDepartmentPresenceState;
            _signalRService.OnUserOnlineStatusChanged += HandleUserOnlineStatusChanged;
            _signalRService.OnErrorMessage += HandleErrorMessage;
        }

        Connectivity.Current.ConnectivityChanged += HandleConnectivityChanged;
        _isSubscribed = true;
    }

    public void UnsubscribeFromEvents()
    {
        if (!_isSubscribed) return;

        if (_signalRService != null)
        {
            _signalRService.OnMessageReceived -= HandleMessageReceived;
            _signalRService.OnMessageAck -= HandleMessageAck;
            _signalRService.OnMessageDelivered -= HandleMessageDelivered;
            _signalRService.OnMessagesRead -= HandleMessagesRead;
            _signalRService.OnMessageDeleted -= HandleMessageDeleted;
            _signalRService.OnInAppToast -= HandleInAppToast;
            _signalRService.OnReconnected -= HandleSignalRReconnected;
            _signalRService.OnUserPresenceChanged -= HandleUserPresenceChanged;
            _signalRService.OnUserTyping -= HandleUserTyping;
            _signalRService.OnUserPresenceState -= HandleUserPresenceState;
            _signalRService.OnDepartmentPresenceState -= HandleDepartmentPresenceState;
            _signalRService.OnUserOnlineStatusChanged -= HandleUserOnlineStatusChanged;
            _signalRService.OnErrorMessage -= HandleErrorMessage;
        }

        Connectivity.Current.ConnectivityChanged -= HandleConnectivityChanged;
        _isSubscribed = false;
    }

    
    private void HandleErrorMessage(string error)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (error.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                IsCurrentUserBlocked = true;
            }
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlertAsync("Notice", error, "OK");
            }
        });
    }

    private async Task LoadLocalMessagesAsync(Guid convId)
    {
        if (_outboxService == null) return;
        var localMessages = await _outboxService.GetRecentConversationMessagesAsync(convId, limit: 50);
        if (localMessages.Count > 0)
        {
            var ordered = localMessages.OrderBy(m => m.SequenceNumber > 0 ? m.SequenceNumber : long.MaxValue).ThenBy(m => m.Timestamp).ToList();
            var items = new List<ChatMessageItem>();
            foreach (var lm in ordered)
            {
                var item = MapLocalToChatItem(lm);
                items.Add(item);
                if (lm.SequenceNumber > 0 && lm.SequenceNumber < _earliestLoadedSequenceNumber)
                {
                    _earliestLoadedSequenceNumber = lm.SequenceNumber;
                }
                if (lm.SequenceNumber > _lastSequenceNumber)
                {
                    _lastSequenceNumber = lm.SequenceNumber;
                }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (Messages.Count == items.Count && Messages.Count > 0 &&
                    Messages[0].Id == items[0].Id && Messages[^1].Id == items[^1].Id)
                {
                    TriggerScrollToBottom();
                    return;
                }

                PreComputeDateHeaders(items);

                Messages.Clear();
                foreach (var item in items)
                {
                    Messages.Add(item);
                }
                TriggerScrollToBottom();
            });
        }
    }

    public async Task InitializeAsync()
    {
        SubscribeToEvents();

        var myUserId = _authService?.UserId ?? Guid.Empty;
        var isCustomer = _authService?.Role == UserRole.User || _authService?.CurrentUser?.Role == UserRole.User;

        // 1. Check if conversation ID is already available or cached
        if (!string.IsNullOrEmpty(ConversationIdString) && Guid.TryParse(ConversationIdString, out var explicitConvId))
        {
            _conversationId = explicitConvId;
            _convIdTcs.TrySetResult(_conversationId);
        }
        else if (isCustomer && Guid.TryParse(DepartmentId, out var deptGuid))
        {
            var cachedConvIdStr = Preferences.Default.Get($"dept_conv_{deptGuid}_{myUserId}", string.Empty);
            if (!string.IsNullOrEmpty(cachedConvIdStr) && Guid.TryParse(cachedConvIdStr, out var cachedConvId))
            {
                _conversationId = cachedConvId;
                _convIdTcs.TrySetResult(_conversationId);
            }
        }

        // 2. If conversation ID is known, render local cached messages immediately (0ms instant display!)
        if (_conversationId != Guid.Empty)
        {
            ActiveConversationId = _conversationId;
            await LoadLocalMessagesAsync(_conversationId);
        }

        if (Messages.Count == 0)
        {
            IsLoadingMessages = true;
            LoadingStatusText = "Loading messages...";

        }
        else
        {
            IsSyncingMessages = true;
        }

        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _disposeCts = new CancellationTokenSource();
        var ct = _disposeCts.Token;

        // 3. Perform network synchronization in background without blocking the UI
        _ = Task.Run(async () =>
        {
            try
            {
                // Yield 400ms so UI thread can complete transition, layout, and bottom scroll with 0% CPU contention!
                await Task.Delay(400, ct);
                if (ct.IsCancellationRequested) return;

                if (isCustomer && Guid.TryParse(DepartmentId, out var deptGuid) && _apiService != null)
                {
                    var convs = await _apiService.GetConversationsForDepartmentAsync(deptGuid);
                    var conv = convs.FirstOrDefault();
                    if (conv != null)
                    {
                        if (_conversationId != Guid.Empty && _conversationId != conv.Id)
                        {
                            var oldId = _conversationId;
                            if (_outboxService != null)
                            {
                                await _outboxService.MigrateConversationIdAsync(oldId, conv.Id);
                            }
                            MainThread.BeginInvokeOnMainThread(() => Messages.Clear());
                        }
                        _conversationId = conv.Id;
                        _convIdTcs.TrySetResult(_conversationId);
                        ActiveConversationId = _conversationId;
                        Preferences.Default.Set($"dept_conv_{deptGuid}_{myUserId}", _conversationId.ToString());
                        if (Messages.Count == 0)
                        {
                            await LoadLocalMessagesAsync(_conversationId);
                        }
                    }
                }

                if (_conversationId == Guid.Empty || ct.IsCancellationRequested) return;

                // 4. Fetch messages immediately from REST API (sub-second response)
                await SyncMissedMessagesAsync();
                if (ct.IsCancellationRequested) return;

                // Dismiss loading indicators immediately once messages are rendered
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsLoadingMessages = false;
                    IsSyncingMessages = false;
                    if (isCustomer && PresenceStatusText == "connecting...")
                    {
                        PresenceStatusText = string.Empty;
                    }
                });

                // 5. Connect to SignalR and enter room in background
                if (_signalRService != null && !ct.IsCancellationRequested)
                {
                    if (!_signalRService.IsConnected && !string.IsNullOrEmpty(_authService?.Token))
                    {
                        try { await _signalRService.ConnectAsync(_authService.Token); } catch { }
                    }

                    if (_signalRService.IsConnected && !ct.IsCancellationRequested)
                    {
                        await _signalRService.EnterRoomAsync(_conversationId);
                    }
                }

                // Flush outbox
                if (!ct.IsCancellationRequested)
                {
                    await FlushOutboxAsync();
                }

                // Initial presence check for other user
                if (OtherUserId == Guid.Empty)
                {
                    var otherMsg = Messages.FirstOrDefault(m => !m.IsOutgoing && m.SenderId != Guid.Empty);
                    if (otherMsg != null)
                    {
                        OtherUserId = otherMsg.SenderId;
                    }
                }

                if (OtherUserId != Guid.Empty && _signalRService != null && _signalRService.IsConnected && !ct.IsCancellationRequested)
                {
                    var presence = await _signalRService.GetPresenceAsync(OtherUserId, _conversationId);
                    if (presence != null && !ct.IsCancellationRequested)
                    {
                        ApplyPresence(presence);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatRoom] Background sync error: {ex.Message}");
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsLoadingMessages = false;
                    IsSyncingMessages = false;
                    if (isCustomer && PresenceStatusText == "connecting...")
                    {
                        PresenceStatusText = string.Empty;
                    }
                });
            }
        }, ct);
    }

    public async Task FlushOutboxAsync()
    {
        if (_outboxService == null || _signalRService == null)
            return;

        var convId = await EnsureConversationIdAsync();
        if (convId == Guid.Empty)
            return;

        try
        {
            var pending = await _outboxService.GetPendingAsync(convId);
            foreach (var p in pending)
            {
                var sent = await _signalRService.SendMessageAsync(convId, p.ClientGeneratedId, p.Content);
                if (!sent)
                {
                    break; // Connection lost or still offline, will retry on next reconnect
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (HasPendingAttachment && !string.IsNullOrEmpty(PendingAttachmentPath))
        {
            var path = PendingAttachmentPath;
            var caption = InputMessage?.Trim();
            InputMessage = string.Empty;
            ClearPendingAttachment();

            if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessAndSendDocumentAsync(path, PendingAttachmentTitle);
            }
            else
            {
                await ProcessAndSendImageAsync(path, caption);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(InputMessage))
            return;

        var text = InputMessage.Trim();
        InputMessage = string.Empty;

        await SendMessageInternalAsync(text, isVoice: false);
    }

    [RelayCommand]
    private async Task StartVoiceRecordingAsync()
    {
        if (_audioService == null) return;

        var status = await Permissions.RequestAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            await Shell.Current.DisplayAlert("Microphone Permission", "Permission is required to record voice messages.", "OK");
            return;
        }

        var fileName = $"voice_{Guid.NewGuid():N}.m4a";
        _activeRecordingPath = Path.Combine(FileSystem.CacheDirectory, fileName);

        var started = await _audioService.StartRecordingAsync(_activeRecordingPath);
        if (started)
        {
            IsRecordingVoice = true;
            IsVoicePreviewReady = false;
            IsVoicePreviewPlaying = false;
            _recordingSeconds = 0;
            RecordingDurationText = "0:00";
            VoicePreviewProgress = 0;

            _recordingTimer?.Stop();
            _recordingTimer = new System.Timers.Timer(1000);
            _recordingTimer.Elapsed += (s, e) =>
            {
                _recordingSeconds++;
                var m = _recordingSeconds / 60;
                var sec = _recordingSeconds % 60;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RecordingDurationText = $"{m}:{sec:D2}";
                });
            };
            _recordingTimer.Start();
        }
    }

    [RelayCommand]
    private async Task PauseAndPreviewVoiceAsync()
    {
        _recordingTimer?.Stop();
        if (_audioService == null) return;

        var path = await _audioService.StopRecordingAsync();
        IsRecordingVoice = false;

        if (string.IsNullOrEmpty(path) || !File.Exists(path) || _recordingSeconds <= 0)
        {
            IsVoicePreviewReady = false;
            return;
        }

        IsVoicePreviewReady = true;
        IsVoicePreviewPlaying = false;
        VoicePreviewProgress = 0;
        VoicePreviewTimeText = $"0:00 / {RecordingDurationText}";
        OnPropertyChanged(nameof(VoicePreviewPlayIcon));
    }

    [RelayCommand]
    private async Task TogglePlayVoicePreviewAsync()
    {
        if (_audioService == null || string.IsNullOrEmpty(_activeRecordingPath) || !File.Exists(_activeRecordingPath))
            return;

        if (IsVoicePreviewPlaying)
        {
            _previewPlaybackTimer?.Stop();
            _previewPlaybackTimer?.Dispose();
            _previewPlaybackTimer = null;
            await _audioService.StopAudioAsync();
            IsVoicePreviewPlaying = false;
            OnPropertyChanged(nameof(VoicePreviewPlayIcon));
            return;
        }

        IsVoicePreviewPlaying = true;
        OnPropertyChanged(nameof(VoicePreviewPlayIcon));

        var totalDuration = Math.Max(1, _recordingSeconds);
        if (VoicePreviewProgress > 0.01 && VoicePreviewProgress < 0.98)
        {
            _previewElapsedSeconds = VoicePreviewProgress * totalDuration;
        }
        else
        {
            VoicePreviewProgress = 0;
            _previewElapsedSeconds = 0;
        }

        _previewPlaybackTimer?.Stop();
        _previewPlaybackTimer?.Dispose();
        _previewPlaybackTimer = new System.Timers.Timer(100);
        _previewPlaybackTimer.Elapsed += (s, e) =>
        {
            if (!_isPreviewDragging)
            {
                _previewElapsedSeconds += 0.1;
                if (_previewElapsedSeconds > totalDuration)
                {
                    _previewElapsedSeconds = totalDuration;
                }
            }
            var progress = Math.Clamp(_previewElapsedSeconds / totalDuration, 0.0, 1.0);
            var curSec = (int)_previewElapsedSeconds;
            var m = curSec / 60;
            var sec = curSec % 60;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (IsVoicePreviewPlaying && !_isPreviewDragging)
                {
                    VoicePreviewProgress = progress;
                    VoicePreviewTimeText = $"{m}:{sec:D2} / {RecordingDurationText}";
                }
            });
        };
        _previewPlaybackTimer.Start();

        var startOffsetMs = (int)(_previewElapsedSeconds * 1000);
        await _audioService.PlayAudioAsync(_activeRecordingPath, () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _previewPlaybackTimer?.Stop();
                _previewPlaybackTimer?.Dispose();
                _previewPlaybackTimer = null;
                IsVoicePreviewPlaying = false;
                VoicePreviewProgress = 0;
                VoicePreviewTimeText = $"0:00 / {RecordingDurationText}";
                OnPropertyChanged(nameof(VoicePreviewPlayIcon));
            });
        }, startOffsetMs);
    }

    public void OnPreviewSliderDragStarted()
    {
        _isPreviewDragging = true;
    }

    public async Task SeekVoicePreviewAsync(double progress)
    {
        var clamped = Math.Clamp(progress, 0.0, 1.0);
        VoicePreviewProgress = clamped;
        var totalDuration = Math.Max(1, _recordingSeconds);
        _previewElapsedSeconds = clamped * totalDuration;
        var curSec = (int)_previewElapsedSeconds;
        var m = curSec / 60;
        var sec = curSec % 60;
        VoicePreviewTimeText = $"{m}:{sec:D2} / {RecordingDurationText}";

        if (_audioService != null && IsVoicePreviewPlaying)
        {
            await _audioService.SeekToAsync(clamped);
        }
        _isPreviewDragging = false;
    }

    public void OnMessageSliderDragStarted(ChatMessageItem item)
    {
        item.IsDragging = true;
    }

    public async Task SeekMessageAudioAsync(ChatMessageItem item, double progress)
    {
        var clamped = Math.Clamp(progress, 0.0, 1.0);
        item.PlaybackProgress = clamped;
        var totalDuration = Math.Max(1, item.VoiceDurationSeconds);
        _playbackElapsedSeconds = clamped * totalDuration;
        var curSec = (int)_playbackElapsedSeconds;
        var m = curSec / 60;
        var sec = curSec % 60;
        item.CurrentPlaybackTime = $"{m}:{sec:D2}";

        if (item.IsPlaying && _audioService != null)
        {
            await _audioService.SeekToAsync(clamped);
        }
        item.IsDragging = false;
    }

    [RelayCommand]
    private async Task CancelVoiceRecordingAsync()
    {
        _recordingTimer?.Stop();
        _previewPlaybackTimer?.Stop();
        _previewPlaybackTimer?.Dispose();
        _previewPlaybackTimer = null;

        IsRecordingVoice = false;
        IsVoicePreviewReady = false;
        IsVoicePreviewPlaying = false;

        if (_audioService != null)
        {
            if (_audioService.IsRecording)
            {
                await _audioService.StopRecordingAsync();
            }
            if (_audioService.IsPlaying)
            {
                await _audioService.StopAudioAsync();
            }
        }

        if (!string.IsNullOrEmpty(_activeRecordingPath) && File.Exists(_activeRecordingPath))
        {
            try { File.Delete(_activeRecordingPath); } catch { }
        }
    }

    [RelayCommand]
    private async Task SendVoiceRecordingAsync()
    {
        _recordingTimer?.Stop();
        _previewPlaybackTimer?.Stop();
        _previewPlaybackTimer?.Dispose();
        _previewPlaybackTimer = null;

        string? path = _activeRecordingPath;

        if (_audioService != null)
        {
            if (IsRecordingVoice)
            {
                path = await _audioService.StopRecordingAsync();
            }
            if (_audioService.IsPlaying)
            {
                await _audioService.StopAudioAsync();
            }
        }

        IsRecordingVoice = false;
        IsVoicePreviewReady = false;
        IsVoicePreviewPlaying = false;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var duration = Math.Max(1, _recordingSeconds);
        var bytes = await File.ReadAllBytesAsync(path);
        var base64 = Convert.ToBase64String(bytes);
        var voicePayload = $"[voice:data:audio/m4a;base64,{base64}:{duration}]";

        await SendMessageInternalAsync(voicePayload, isVoice: true, duration: duration, localPath: path);
    }

    [RelayCommand]
    private async Task TogglePlayVoiceAsync(ChatMessageItem item)
    {
        if (_audioService == null) return;

        if (item.IsPlaying)
        {
            _playbackTimer?.Stop();
            _playbackTimer?.Dispose();
            _playbackTimer = null;
            await _audioService.StopAudioAsync();
            item.IsPlaying = false;
            _currentlyPlayingItem = null;
            return;
        }

        if (_currentlyPlayingItem != null)
        {
            _playbackTimer?.Stop();
            _playbackTimer?.Dispose();
            _playbackTimer = null;
            _currentlyPlayingItem.IsPlaying = false;
            _currentlyPlayingItem.PlaybackProgress = 0;
            _currentlyPlayingItem.CurrentPlaybackTime = _currentlyPlayingItem.VoiceDurationText;
            await _audioService.StopAudioAsync();
            _currentlyPlayingItem = null;
        }

        // Ensure local audio file exists from base64
        var audioPath = item.LocalAudioPath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            try
            {
                var content = item.Content;
                if (content.StartsWith("[voice:data:audio/m4a;base64,", StringComparison.OrdinalIgnoreCase))
                {
                    var prefixLen = "[voice:data:audio/m4a;base64,".Length;
                    var lastColon = content.LastIndexOf(':');
                    if (lastColon > prefixLen)
                    {
                        var base64 = content.Substring(prefixLen, lastColon - prefixLen);
                        var bytes = Convert.FromBase64String(base64);
                        var cacheFile = Path.Combine(FileSystem.CacheDirectory, $"{item.ClientGeneratedId}.m4a");
                        await File.WriteAllBytesAsync(cacheFile, bytes);
                        item.LocalAudioPath = cacheFile;
                        audioPath = cacheFile;
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            _currentlyPlayingItem = item;
            item.IsPlaying = true;
            var totalDuration = Math.Max(1, item.VoiceDurationSeconds);

            if (item.PlaybackProgress > 0.01 && item.PlaybackProgress < 0.98)
            {
                _playbackElapsedSeconds = item.PlaybackProgress * totalDuration;
            }
            else
            {
                item.PlaybackProgress = 0;
                _playbackElapsedSeconds = 0;
            }

            _playbackTimer?.Stop();
            _playbackTimer?.Dispose();
            _playbackTimer = new System.Timers.Timer(100);
            _playbackTimer.Elapsed += (s, e) =>
            {
                if (!item.IsDragging)
                {
                    _playbackElapsedSeconds += 0.1;
                    if (_playbackElapsedSeconds > totalDuration)
                    {
                        _playbackElapsedSeconds = totalDuration;
                    }
                }
                var progress = Math.Clamp(_playbackElapsedSeconds / totalDuration, 0.0, 1.0);
                var curSec = (int)_playbackElapsedSeconds;
                var m = curSec / 60;
                var sec = curSec % 60;
                var timeText = $"{m}:{sec:D2}";

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (item.IsPlaying && !item.IsDragging)
                    {
                        item.PlaybackProgress = progress;
                        item.CurrentPlaybackTime = timeText;
                    }
                });
            };
            _playbackTimer.Start();

            var startOffsetMs = (int)(_playbackElapsedSeconds * 1000);
            await _audioService.PlayAudioAsync(audioPath, () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _playbackTimer?.Stop();
                    _playbackTimer?.Dispose();
                    _playbackTimer = null;
                    item.IsPlaying = false;
                    item.PlaybackProgress = 0;
                    item.CurrentPlaybackTime = item.VoiceDurationText;
                    _currentlyPlayingItem = null;
                });
            }, startOffsetMs);
        }
    }

    private Task SendMessageInternalAsync(string content, bool isVoice = false, int duration = 0, string? localPath = null)
    {
        _typingTimer?.Stop();
        if (_isCurrentlyTypingSent)
        {
            _isCurrentlyTypingSent = false;
            if (_signalRService != null && _conversationId != Guid.Empty)
            {
                _ = _signalRService.SendTypingAsync(_conversationId, false);
            }
        }

        var clientMsgId = Guid.NewGuid().ToString();
        var myUserId = _authService?.UserId ?? Guid.Empty;
        var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
        var myName = _authService?.CurrentUser?.FullName ?? "You";

        // Optimistic UI update: instant speech bubble
        var pendingItem = new ChatMessageItem
        {
            ClientGeneratedId = clientMsgId,
            Content = content,
            TimeFormatted = DateTime.Now.ToString("h:mm tt"),
            Status = MessageDeliveryStatus.Pending,
            StatusIcon = "🕒",
            StatusColor = ChatMessageItem.StatusSentColor,
            IsVoiceMessage = isVoice,
            VoiceDurationSeconds = duration > 0 ? duration : 5,
            VoiceDurationText = isVoice ? $"0:{duration:D2}" : string.Empty,
            CurrentPlaybackTime = isVoice ? $"0:{duration:D2}" : string.Empty,
            LocalAudioPath = localPath
        };
        ApplyRoleAwareAlignment(pendingItem, myUserId, myRole, myName);

        Messages.Add(pendingItem);
        UpdateDateHeaders();
        RequestScrollToBottom?.Invoke();

        // Store to local SQLite outbox & stream via SignalR in background so UI thread remains silky smooth
        _ = Task.Run(async () =>
        {
            await _outgoingSendLock.WaitAsync();
            try
            {
                var convId = await EnsureConversationIdAsync();
                if (convId == Guid.Empty)
                {
                    System.Diagnostics.Debug.WriteLine("[ChatRoom] Failed to resolve conversation ID for send.");
                    return;
                }

                if (_outboxService != null)
                {
                    await _outboxService.EnqueueAsync(convId, clientMsgId, content, myName, myUserId, myRole);
                }

                if (_signalRService != null)
                {
                    await _signalRService.SendMessageAsync(convId, clientMsgId, content);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatRoom] Send error: {ex.Message}");
            }
            finally
            {
                _outgoingSendLock.Release();
            }
        });

        return Task.CompletedTask;
    }

    private string FormatLastSeen(DateTime? lastSeenUtc)
    {
        var dt = (lastSeenUtc ?? DateTime.UtcNow).ToLocalTime();
        if (dt.Date == DateTime.Today)
            return $"last seen today at {dt:h:mm tt}";
        if (dt.Date == DateTime.Today.AddDays(-1))
            return $"last seen yesterday at {dt:h:mm tt}";
        return $"last seen {dt:MMM dd} at {dt:h:mm tt}";
    }

    private void ApplyPresence(UserPresenceDto presence)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (presence.IsInRoom || presence.IsOnline)
            {
                IsOtherUserOnline = true;
                PresenceStatusText = "online";
                PresenceTextColor = Color.FromArgb("#A7F3D0");
            }
            else if (IsCustomer)
            {
                // Customer viewing department: if no staff online, show nothing
                IsOtherUserOnline = false;
                PresenceStatusText = string.Empty;
            }
            else if (presence.LastSeenAt.HasValue)
            {
                // Admin/Manager viewing customer: show last seen
                IsOtherUserOnline = false;
                PresenceStatusText = FormatLastSeen(presence.LastSeenAt.Value);
                PresenceTextColor = Color.FromArgb("#D8E2DC");
            }
            else
            {
                IsOtherUserOnline = false;
                PresenceStatusText = FormatLastSeen(DateTime.UtcNow);
                PresenceTextColor = Color.FromArgb("#D8E2DC");
            }
        });
    }

    private void HandleUserPresenceChanged(Guid convId, Guid userId, bool isInRoom)
    {
        if (convId != _conversationId) return;
        var myUserId = _authService?.UserId ?? Guid.Empty;
        if (userId == myUserId) return;

        // If staff viewer, strictly ignore presence for anyone other than Customer
        if (!IsCustomer && OtherUserId != Guid.Empty && userId != OtherUserId) return;

        if (OtherUserId == Guid.Empty)
        {
            OtherUserId = userId;
        }
        if (isInRoom)
        {
            IsOtherUserOnline = true;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isInRoom || IsOtherUserOnline)
            {
                PresenceStatusText = "online";
                PresenceTextColor = Color.FromArgb("#A7F3D0");
            }
            else if (IsCustomer)
            {
                PresenceStatusText = string.Empty;
            }
            else
            {
                PresenceStatusText = $"last seen today at {DateTime.Now:h:mm tt}";
                PresenceTextColor = Color.FromArgb("#D8E2DC");
            }
        });
    }

    private void HandleUserTyping(Guid convId, Guid userId, string userName, bool isTyping)
    {
        if (convId != _conversationId) return;
        var myUserId = _authService?.UserId ?? Guid.Empty;
        if (userId == myUserId) return;

        // If staff viewer, strictly ignore typing from colleague staff members
        if (!IsCustomer && OtherUserId != Guid.Empty && userId != OtherUserId) return;

        if (OtherUserId == Guid.Empty)
        {
            OtherUserId = userId;
        }
        IsOtherUserTyping = isTyping;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isTyping)
            {
                PresenceStatusText = "typing...";
                PresenceTextColor = Color.FromArgb("#25D366");
            }
            else
            {
                if (IsOtherUserOnline)
                {
                    PresenceStatusText = "online";
                    PresenceTextColor = Color.FromArgb("#A7F3D0");
                }
                else if (IsCustomer)
                {
                    PresenceStatusText = string.Empty;
                }
                else
                {
                    PresenceStatusText = $"last seen today at {DateTime.Now:h:mm tt}";
                    PresenceTextColor = Color.FromArgb("#D8E2DC");
                }
            }
        });
    }

    private void HandleUserPresenceState(Guid convId, Guid userId, bool isInRoom, bool isOnline, DateTime? lastSeen)
    {
        if (convId != _conversationId) return;
        var myUserId = _authService?.UserId ?? Guid.Empty;
        if (userId == myUserId) return;

        // If staff viewer, strictly ignore presence updates for anyone other than the Customer (OtherUserId)!
        if (!IsCustomer && OtherUserId != Guid.Empty && userId != OtherUserId) return;

        if (OtherUserId == Guid.Empty)
        {
            OtherUserId = userId;
        }
        ApplyPresence(new UserPresenceDto(userId, isOnline, isInRoom, lastSeen));
    }

    private void HandleDepartmentPresenceState(Guid convId, Guid deptId, bool isOnline)
    {
        if (convId != _conversationId) return;
        if (!IsCustomer) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isOnline)
            {
                PresenceStatusText = "online";
                PresenceTextColor = Color.FromArgb("#A7F3D0");
                IsOtherUserOnline = true;
            }
            else
            {
                // Customer viewing department: no staff online -> show nothing
                PresenceStatusText = string.Empty;
                IsOtherUserOnline = false;
            }
        });
    }

    private void HandleUserOnlineStatusChanged(Guid userId, bool isOnline)
    {
        if (OtherUserId == Guid.Empty || userId != OtherUserId) return;

        IsOtherUserOnline = isOnline;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isOnline)
            {
                PresenceStatusText = "online";
                PresenceTextColor = Color.FromArgb("#A7F3D0");
            }
            else if (IsCustomer)
            {
                // Customer viewing department: staff went offline -> show nothing
                PresenceStatusText = string.Empty;
            }
            else
            {
                // Admin/Manager viewing customer: customer went offline -> show last seen
                PresenceStatusText = $"last seen today at {DateTime.Now:h:mm tt}";
                PresenceTextColor = Color.FromArgb("#D8E2DC");
            }
        });
    }

    [RelayCommand]
    private async Task AudioCallAsync()
    {
        await Shell.Current.DisplayAlert("WhatsApp Voice Call", $"Calling {ContactTitle} via encrypted audio...", "End Call");
    }

    [RelayCommand]
    private async Task VideoCallAsync()
    {
        await Shell.Current.DisplayAlert("WhatsApp Video Call", $"Calling {ContactTitle} via encrypted HD video...", "End Call");
    }

    [RelayCommand]
    private async Task OpenMenuAsync()
    {
        var action = await Shell.Current.DisplayActionSheet(ContactTitle, "Cancel", null, "View Contact Info", "Clear Messages", "Log Out");
        if (action == "Log Out")
        {
            await LogoutAsync();
        }
        else if (action == "Clear Messages")
        {
            Messages.Clear();
        }
        else if (action == "View Contact Info")
        {
            await Shell.Current.DisplayAlert(ContactTitle, $"Department: {DepartmentName}\nStatus: {PresenceStatusText}", "OK");
        }
    }

        [RelayCommand]
    private async Task EmojiStickerAsync()
    {
        var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
        if (myRole != UserRole.Admin && myRole != UserRole.Manager)
        {
            // Only Admin or Manager sends the deposit done sticker
            return;
        }

        try
        {
            var cachePath = Path.Combine(FileSystem.CacheDirectory, "sticker_deposit_done.jpg");
            if (!File.Exists(cachePath) || new FileInfo(cachePath).Length == 0)
            {
                using var assetStream = await FileSystem.OpenAppPackageFileAsync("sticker_deposit_done.jpg");
                using var targetStream = File.Create(cachePath);
                await assetStream.CopyToAsync(targetStream);
            }

            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            {
                await ProcessAndSendImageAsync(cachePath, null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoom] EmojiSticker error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        var action = await Shell.Current.DisplayActionSheet("Share Attachment", "Cancel", null, "📷 Camera", "🖼️ Gallery", "📄 Document");
        if (string.IsNullOrEmpty(action) || action == "Cancel") return;

        try
        {
            if (action == "📷 Camera")
            {
                await CapturePhotoAsync();
            }
            else if (action == "🖼️ Gallery")
            {
                var photo = await MediaPicker.Default.PickPhotoAsync();
                if (photo != null)
                {
                    await ProcessAndSendImageAsync(photo.FullPath);
                }
            }
            else if (action == "📄 Document")
            {
                var file = await FilePicker.Default.PickAsync();
                if (file != null)
                {
                    await ProcessAndSendDocumentAsync(file.FullPath, file.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoom] AttachFile error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CapturePhotoAsync()
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                await Shell.Current.DisplayAlert("Camera Permission", "Camera permission is required to capture photos.", "OK");
                return;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo != null)
            {
                await ProcessAndSendImageAsync(photo.FullPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoom] CapturePhoto error: {ex.Message}");
        }
    }

    private async Task ProcessAndSendImageAsync(string localPath, string? caption = null)
    {
        if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) return;

        // 1. Compress image client-side to ~200KB (max 1280px, 75% quality)
        var compressedPath = await MediaHelper.CompressImageAsync(localPath, 1280, 75);

        var clientMsgId = Guid.NewGuid().ToString();
        var timeFormatted = DateTime.Now.ToString("h:mm tt");
        var myUserId = _authService?.UserId ?? Guid.Empty;
        var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
        var senderName = _authService?.CurrentUser?.FullName ?? "You";

        // 2. Instant optimistic bubble with local thumbnail
        var pendingItem = new ChatMessageItem
        {
            ClientGeneratedId = clientMsgId,
            Content = "[image:uploading...]",
            TimeFormatted = timeFormatted,
            Status = MessageDeliveryStatus.Pending,
            StatusIcon = "🕒",
            StatusColor = ChatMessageItem.StatusSentColor,
            IsImageMessage = true,
            ImageUrl = compressedPath,
            ImageCaption = caption ?? string.Empty
        };
        ApplyRoleAwareAlignment(pendingItem, myUserId, myRole, senderName);

        Messages.Add(pendingItem);
        UpdateDateHeaders();
        RequestScrollToBottom?.Invoke();

        // 3. Upload to server in background, then broadcast via SignalR
        _ = Task.Run(async () =>
        {
            await _outgoingSendLock.WaitAsync();
            try
            {
                var convId = await EnsureConversationIdAsync();
                if (convId == Guid.Empty || _apiService == null) return;

                using var fileStream = File.OpenRead(compressedPath);
                var uploadResult = await _apiService.UploadAttachmentAsync(fileStream, Path.GetFileName(compressedPath), "image/jpeg");
                if (uploadResult == null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        pendingItem.StatusColor = Colors.Red;
                    });
                    return;
                }

                var imagePayload = string.IsNullOrWhiteSpace(caption)
                    ? $"[image:{uploadResult.Url}]"
                    : $"[image:{uploadResult.Url}:{caption}]";
                pendingItem.Content = imagePayload;
                pendingItem.ImageUrl = _apiService.ResolveFullMediaUrl(uploadResult.Url);

                if (_outboxService != null)
                {
                    await _outboxService.EnqueueAsync(convId, clientMsgId, imagePayload, senderName, myUserId, myRole);
                }

                if (_signalRService != null)
                {
                    await _signalRService.SendMessageAsync(convId, clientMsgId, imagePayload);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatRoom] ProcessAndSendImage error: {ex.Message}");
            }
            finally
            {
                _outgoingSendLock.Release();
            }
        });
    }

    private async Task ProcessAndSendDocumentAsync(string localPath, string originalFileName)
    {
        if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) return;

        var clientMsgId = Guid.NewGuid().ToString();
        var timeFormatted = DateTime.Now.ToString("h:mm tt");
        var myUserId = _authService?.UserId ?? Guid.Empty;
        var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
        var senderName = _authService?.CurrentUser?.FullName ?? "You";
        var fileInfo = new FileInfo(localPath);
        var sizeBytes = fileInfo.Length;

        // Instant optimistic bubble
        var pendingItem = new ChatMessageItem
        {
            ClientGeneratedId = clientMsgId,
            Content = $"[file:uploading:{originalFileName}:{sizeBytes}]",
            TimeFormatted = timeFormatted,
            Status = MessageDeliveryStatus.Pending,
            StatusIcon = "🕒",
            StatusColor = ChatMessageItem.StatusSentColor,
            IsDocumentMessage = true,
            DocumentFileName = originalFileName,
            DocumentFileSizeText = FormatFileSize(sizeBytes),
            DocumentUrl = localPath
        };
        ApplyRoleAwareAlignment(pendingItem, myUserId, myRole, senderName);

        Messages.Add(pendingItem);
        UpdateDateHeaders();
        RequestScrollToBottom?.Invoke();

        _ = Task.Run(async () =>
        {
            await _outgoingSendLock.WaitAsync();
            try
            {
                var convId = await EnsureConversationIdAsync();
                if (convId == Guid.Empty || _apiService == null) return;

                using var fileStream = File.OpenRead(localPath);
                var uploadResult = await _apiService.UploadAttachmentAsync(fileStream, originalFileName, "application/octet-stream");
                if (uploadResult == null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        pendingItem.StatusColor = Colors.Red;
                    });
                    return;
                }

                var docPayload = $"[file:{uploadResult.Url}:{originalFileName}:{sizeBytes}]";
                pendingItem.Content = docPayload;
                pendingItem.DocumentUrl = _apiService.ResolveFullMediaUrl(uploadResult.Url);

                if (_outboxService != null)
                {
                    await _outboxService.EnqueueAsync(convId, clientMsgId, docPayload, senderName, myUserId, myRole);
                }

                if (_signalRService != null)
                {
                    await _signalRService.SendMessageAsync(convId, clientMsgId, docPayload);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatRoom] ProcessAndSendDocument error: {ex.Message}");
            }
            finally
            {
                _outgoingSendLock.Release();
            }
        });
    }

    [RelayCommand]
    private async Task OpenImageAsync(ChatMessageItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.ImageUrl)) return;
        try
        {
            await Shell.Current.Navigation.PushModalAsync(new Views.ImageViewerPage(item.ImageUrl));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoom] OpenImage error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenDocumentAsync(ChatMessageItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.DocumentUrl)) return;
        try
        {
            if (Uri.TryCreate(item.DocumentUrl, UriKind.Absolute, out var uri))
            {
                await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoom] OpenDocument error: {ex.Message}");
        }
    }

    private void HandleMessageReceived(ChatMessageDto msg)
    {
        if (msg.ConversationId != _conversationId) return;

        if (msg.SenderId != (_authService?.UserId ?? Guid.Empty))
        {
            // For staff viewers, only set OtherUserId if it was empty AND message is from customer
            if (OtherUserId == Guid.Empty && msg.SenderRole == UserRole.User)
            {
                OtherUserId = msg.SenderId;
            }
            else if (IsCustomer && OtherUserId == Guid.Empty)
            {
                OtherUserId = msg.SenderId;
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            AddOrUpdateServerMessage(msg);
        });

        // Sequence gap detection: If received message sequence > highest contiguous + 1,
        // one or more messages were missed while offline/interrupted. Fetch missing delta immediately!
        long highestContiguous;
        lock (Messages)
        {
            highestContiguous = GetHighestContiguousSequence(Messages.Select(m => m.SequenceNumber));
        }
        if (msg.SequenceNumber > highestContiguous + 1)
        {
            _ = Task.Run(async () => await SyncMissedMessagesAsync());
        }

        if (_signalRService != null && msg.SenderId != (_authService?.UserId ?? Guid.Empty))
        {
            var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
            var isStaffViewer = myRole == UserRole.Admin || myRole == UserRole.Manager;

            // Delivery and Read receipts must strictly isolate roles:
            // Staff ONLY acknowledges delivery and marks as read if message is from Customer (never colleague staff messages!)
            // Customer ONLY acknowledges delivery and marks as read if message is from Staff!
            bool shouldAcknowledge = isStaffViewer 
                ? (msg.SenderRole == UserRole.User)
                : (msg.SenderRole != UserRole.User);

            if (shouldAcknowledge)
            {
                if (ActiveConversationId == _conversationId)
                {
                    // Recipient is actively viewing this room:
                    // If not already marked as Read by server, mark as Read immediately (never trigger intermediate double grey ticks)
                    if (msg.Status < MessageDeliveryStatus.Read)
                    {
                        _ = _signalRService.MarkAsReadAsync(_conversationId, msg.SequenceNumber);
                    }
                }
                else
                {
                    // Recipient device received message while outside the room (in background/home):
                    // Acknowledge delivery so sender sees double grey ticks until room is opened
                    if (msg.Status < MessageDeliveryStatus.Delivered)
                    {
                        _ = _signalRService.AcknowledgeDeliveryAsync(msg.Id);
                    }
                }
            }
        }

        if (_outboxService != null)
        {
            _ = _outboxService.SaveServerMessageAsync(
                _conversationId,
                msg.Id,
                msg.SenderId,
                msg.SenderRole,
                msg.ClientGeneratedId,
                msg.Content,
                msg.SenderName,
                msg.SenderId == (_authService?.UserId ?? Guid.Empty),
                msg.SequenceNumber,
                msg.Status,
                msg.SentAt
            );
        }
    }

    private void ApplyRoleAwareAlignment(ChatMessageItem item, Guid senderId, UserRole senderRole, string senderName)
    {
        var myUserId = _authService?.UserId ?? Guid.Empty;
        var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
        var isStaffViewer = myRole == UserRole.Admin || myRole == UserRole.Manager;

        item.SenderId = senderId;
        item.SenderRole = senderRole;
        item.SenderName = senderName;

        // Staff message: either explicit role Admin/Manager, or known System Admin ID, or sent by me while I am staff
        var isStaffMessage = (senderRole == UserRole.Admin || senderRole == UserRole.Manager) ||
                             (senderId == Guid.Parse("11111111-1111-1111-1111-111111111111")) ||
                             (senderId == myUserId && isStaffViewer);

        if (isStaffViewer)
        {
            // Staff view: ALL staff messages appear on SENDER side (Right / Green)
            if (isStaffMessage || senderId == myUserId)
            {
                item.IsOutgoing = true;
                // Colleague staff message on sender side shows who texted what!
                item.ShowSenderName = (senderId != myUserId) && !string.IsNullOrWhiteSpace(senderName);
            }
            else
            {
                // Customer message appears on RECEIVER side (Left / White)
                item.IsOutgoing = false;
                item.ShowSenderName = false;
            }
        }
        else
        {
            // Customer view: Customer messages on Right, Staff messages on Left with staff name
            if (senderRole == UserRole.User || senderId == myUserId)
            {
                item.IsOutgoing = true;
                item.ShowSenderName = false;
            }
            else
            {
                item.IsOutgoing = false;
                item.ShowSenderName = !string.IsNullOrWhiteSpace(senderName);
            }
        }
    }

    private void AddOrUpdateServerMessage(ChatMessageDto msg, bool reorder = true, bool scroll = true)
    {
        var existing = Messages.FirstOrDefault(m => m.ClientGeneratedId == msg.ClientGeneratedId || m.Id == msg.Id);
        if (existing != null)
        {
            existing.Id = msg.Id;
            existing.SequenceNumber = msg.SequenceNumber;
            ApplyRoleAwareAlignment(existing, msg.SenderId, msg.SenderRole, msg.SenderName);
            UpdateStatusVisuals(existing, msg.Status);
            if (reorder) ReorderMessageIfNeeded(existing);
        }
        else
        {
            var newItem = new ChatMessageItem
            {
                Id = msg.Id,
                ClientGeneratedId = msg.ClientGeneratedId,
                Content = msg.Content,
                TimeFormatted = msg.SentAt.ToLocalTime().ToString("h:mm tt"),
                MessageDate = msg.SentAt.ToLocalTime(),
                SequenceNumber = msg.SequenceNumber,
                IsDeleted = msg.IsDeleted,
                IsForwarded = msg.IsForwarded
            };

            ApplyRoleAwareAlignment(newItem, msg.SenderId, msg.SenderRole, msg.SenderName);
            ParseMessagePayload(newItem, msg.Content);
            UpdateStatusVisuals(newItem, msg.Status);

            Messages.Add(newItem);
            if (reorder) SortMessages();
            if (scroll) RequestScrollToBottom?.Invoke();
        }

        if (msg.SequenceNumber > 0 && msg.SequenceNumber < _earliestLoadedSequenceNumber)
        {
            _earliestLoadedSequenceNumber = msg.SequenceNumber;
        }

        if (msg.SequenceNumber > _lastSequenceNumber)
        {
            _lastSequenceNumber = msg.SequenceNumber;
        }
    }

    private ChatMessageItem MapLocalToChatItem(LocalOutboxMessage lm)
    {
        var item = new ChatMessageItem
        {
            Id = lm.ServerMessageId ?? Guid.NewGuid(),
            ClientGeneratedId = lm.ClientGeneratedId,
            Content = lm.Content,
            TimeFormatted = lm.Timestamp.ToLocalTime().ToString("h:mm tt"),
            MessageDate = lm.Timestamp.ToLocalTime(),
            SequenceNumber = lm.SequenceNumber,
            IsDeleted = lm.IsDeleted,
            IsForwarded = lm.IsForwarded
        };

        ApplyRoleAwareAlignment(item, lm.SenderId, lm.SenderRole, lm.SenderName);
        ParseMessagePayload(item, lm.Content);
        UpdateStatusVisuals(item, lm.Status);
        return item;
    }

    private void ParseMessagePayload(ChatMessageItem item, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        if (content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase))
        {
            item.IsVoiceMessage = true;
            try
            {
                // Format: [voice:<base64 audio>:<seconds>]. Read only the duration at the end,
                // without copying the (often very large) audio data.
                var end = content.EndsWith("]") ? content.Length - 1 : content.Length;
                var lastColon = content.LastIndexOf(':', end - 1);
                if (lastColon > 6)
                {
                    var dur = content.Substring(lastColon + 1, end - lastColon - 1);
                    if (int.TryParse(dur, out var s))
                    {
                        var m = s / 60;
                        var sec = s % 60;
                        item.VoiceDurationSeconds = s;
                        item.VoiceDurationText = $"{m}:{sec:D2}";
                        item.CurrentPlaybackTime = item.VoiceDurationText;
                        return;
                    }
                }
            }
            catch { }
            item.VoiceDurationSeconds = 5;
            item.VoiceDurationText = "0:05";
            item.CurrentPlaybackTime = "0:05";
            return;
        }

        if (content.StartsWith("[image:", StringComparison.OrdinalIgnoreCase) && content.EndsWith("]"))
        {
            item.IsImageMessage = true;
            try
            {
                var inner = content.Substring(7, content.Length - 8);
                var firstColon = inner.IndexOf(':');
                string url;
                string caption = string.Empty;
                if (firstColon > 0)
                {
                    url = inner.Substring(0, firstColon);
                    caption = inner.Substring(firstColon + 1);
                }
                else
                {
                    url = inner;
                }

                item.ImageUrl = _apiService != null ? _apiService.ResolveFullMediaUrl(url) : url;
                item.ImageCaption = caption;
            }
            catch { }
            return;
        }

        if (content.StartsWith("[file:", StringComparison.OrdinalIgnoreCase) && content.EndsWith("]"))
        {
            item.IsDocumentMessage = true;
            try
            {
                var inner = content.Substring(6, content.Length - 7);
                var parts = inner.Split(':');
                if (parts.Length >= 2)
                {
                    var url = parts[0];
                    item.DocumentUrl = _apiService != null ? _apiService.ResolveFullMediaUrl(url) : url;
                    item.DocumentFileName = parts[1];
                    if (parts.Length >= 3 && long.TryParse(parts[2], out var bytes))
                    {
                        item.DocumentFileSizeText = FormatFileSize(bytes);
                    }
                    else
                    {
                        item.DocumentFileSizeText = "Document";
                    }
                }
            }
            catch { }
            return;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
    }

    private void HandleMessageAck(string clientGenId, Guid serverMsgId, long sequenceNumber, MessageDeliveryStatus status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = Messages.FirstOrDefault(m => m.ClientGeneratedId == clientGenId);
            if (existing != null)
            {
                existing.Id = serverMsgId;
                existing.SequenceNumber = sequenceNumber;
                UpdateStatusVisuals(existing, status);
                ReorderMessageIfNeeded(existing);
            }
        });

        if (_outboxService != null)
        {
            _ = _outboxService.UpdateAckAsync(clientGenId, serverMsgId, sequenceNumber, status);
        }
    }

    private void HandleMessageDelivered(Guid serverMsgId)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = Messages.FirstOrDefault(m => m.Id == serverMsgId);
            if (existing != null && existing.StatusColor != Color.FromArgb("#34B7F1"))
            {
                UpdateStatusVisuals(existing, MessageDeliveryStatus.Delivered);
            }
        });

        if (_outboxService != null)
        {
            _ = _outboxService.UpdateDeliveredAsync(serverMsgId);
        }
    }

    private void HandleMessagesRead(Guid convId, long maxSeq)
    {
        if (convId != _conversationId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var m in Messages.Where(m => m.IsOutgoing && m.SequenceNumber > 0 && m.SequenceNumber <= maxSeq))
            {
                UpdateStatusVisuals(m, MessageDeliveryStatus.Read);
            }
        });

        if (_outboxService != null)
        {
            _ = _outboxService.UpdateReadAsync(convId, maxSeq);
        }
    }

    private void HandleSignalRReconnected()
    {
        _ = Task.Run(async () =>
        {
            if (_conversationId != Guid.Empty && _signalRService != null)
            {
                try
                {
                    await _signalRService.EnterRoomAsync(_conversationId);
                    await SyncMissedMessagesAsync();
                    await FlushOutboxAsync();
                }
                catch { }
            }
        });
    }

    private void HandleConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_signalRService != null && !_signalRService.IsConnected && !string.IsNullOrEmpty(_authService?.Token))
                    {
                        await _signalRService.ConnectAsync(_authService.Token);
                    }
                    if (_conversationId != Guid.Empty && _signalRService != null && _signalRService.IsConnected)
                    {
                        await _signalRService.EnterRoomAsync(_conversationId);
                        await SyncMissedMessagesAsync();
                        await FlushOutboxAsync();
                    }
                }
                catch { }
            });
        }
    }

    private void HandleInAppToast(InAppNotificationToastDto toast)
    {
        if (ActiveConversationId != Guid.Empty && toast.ConversationId == ActiveConversationId)
        {
            return;
        }

        if (_conversationId != Guid.Empty && toast.ConversationId == _conversationId)
        {
            return;
        }

        if (Guid.TryParse(DepartmentId, out var currentDeptId) && toast.DepartmentId == currentDeptId)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            InAppToastMessage = $"🔔 {toast.DepartmentName}: {toast.SenderName}: {DepartmentConversationsViewModel.SanitizePreview(toast.ContentPreview)}";
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

    [RelayCommand]
    private void DismissToast()
    {
        ShowInAppToast = false;
    }

    private void UpdateStatusVisuals(ChatMessageItem item, MessageDeliveryStatus status)
    {
        if (status < item.Status)
            return;

        item.Status = status;
        switch (status)
        {
            case MessageDeliveryStatus.Pending:
                item.StatusIcon = "🕒";
                item.StatusColor = ChatMessageItem.StatusSentColor;
                break;
            case MessageDeliveryStatus.Sent:
                item.StatusIcon = "✓";
                item.StatusColor = Colors.Gray;
                break;
            case MessageDeliveryStatus.Delivered:
                item.StatusIcon = "✓✓";
                item.StatusColor = Colors.Gray;
                break;
            case MessageDeliveryStatus.Read:
                item.StatusIcon = "✓✓";
                item.StatusColor = ChatMessageItem.StatusReadColor; // WhatsApp read blue tick
                break;
        }
    }

    public async Task OnDisappearingAsync()
    {
        _disposeCts.Cancel();
        if (ActiveConversationId == _conversationId)
        {
            ActiveConversationId = Guid.Empty;
        }

        UnsubscribeFromEvents();

        _typingTimer?.Stop();
        _typingTimer?.Dispose();
        _typingTimer = null;
        if (_isCurrentlyTypingSent && _signalRService != null && _conversationId != Guid.Empty)
        {
            _isCurrentlyTypingSent = false;
            try { await _signalRService.SendTypingAsync(_conversationId, false); } catch { }
        }

        if (_signalRService != null && _conversationId != Guid.Empty)
        {
            try
            {
                await _signalRService.LeaveRoomAsync(_conversationId);
            }
            catch { }
        }

        _playbackTimer?.Stop();
        _playbackTimer?.Dispose();
        _playbackTimer = null;

        _previewPlaybackTimer?.Stop();
        _previewPlaybackTimer?.Dispose();
        _previewPlaybackTimer = null;

        if (_audioService != null)
        {
            await _audioService.StopAudioAsync();
        }
        if (_currentlyPlayingItem != null)
        {
            _currentlyPlayingItem.IsPlaying = false;
            _currentlyPlayingItem.PlaybackProgress = 0;
            _currentlyPlayingItem.CurrentPlaybackTime = _currentlyPlayingItem.VoiceDurationText;
            _currentlyPlayingItem = null;
        }
    }

    [RelayCommand]
    public async Task BackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        await OnDisappearingAsync();
        if (_signalRService != null)
        {
            try
            {
                if (_conversationId != Guid.Empty)
                {
                    await _signalRService.LeaveRoomAsync(_conversationId);
                }
                await _signalRService.DisconnectAsync();
            }
            catch { }
        }

        _authService?.Logout();
        await Shell.Current.GoToAsync("//LoginPage");
    }

    private void HandleMessageDeleted(Guid convId, Guid messageId)
    {
        if (convId != _conversationId) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = Messages.FirstOrDefault(m => m.Id == messageId);
            if (item != null)
            {
                item.IsDeleted = true;
                item.Content = "🚫 This message was deleted";
                item.IsImageMessage = false;
                item.IsVoiceMessage = false;
                item.IsDocumentMessage = false;
                item.ImageUrl = null;
                item.LocalAudioPath = null;
                item.DocumentUrl = null;
            }
        });

        if (_outboxService != null)
        {
            _ = _outboxService.MarkMessageDeletedAsync(messageId);
        }
    }

    [RelayCommand]
    private void ShowMessageMenu(ChatMessageItem? item)
    {
        if (item == null) return;

        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch { }

        try
        {
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(50));
        }
        catch { }

        var myUserId = _authService?.UserId ?? Guid.Empty;
        var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;

        SelectedMenuMessage = item;
        SelectedMessageSenderName = item.IsOutgoing ? "You" : (string.IsNullOrWhiteSpace(item.SenderName) ? "User" : item.SenderName);
        SelectedMessageTime = item.TimeFormatted;
        SelectedMessagePreviewText = !string.IsNullOrWhiteSpace(item.Content) 
            ? item.Content 
            : (item.IsVoiceMessage ? "🎙️ Voice message" : (item.IsImageMessage ? "📷 Image" : (item.IsDocumentMessage ? "📄 Document" : "Message")));

        CanCopySelectedMessage = !item.IsDeleted && !string.IsNullOrWhiteSpace(item.Content);
        CanForwardSelectedMessage = myRole == UserRole.Admin && !item.IsDeleted;
        CanDeleteSelectedMessage = !item.IsDeleted && (
            myRole == UserRole.Admin ||
            (myRole == UserRole.Manager && (item.SenderId == myUserId || item.IsOutgoing))
        );

        IsMessageMenuVisible = true;
    }

    [RelayCommand]
    private void CloseMessageMenu()
    {
        IsMessageMenuVisible = false;
        SelectedMenuMessage = null;
    }

    [RelayCommand]
    private async Task CopyMessageFromMenuAsync()
    {
        var msg = SelectedMenuMessage;
        IsMessageMenuVisible = false;
        if (msg != null && !string.IsNullOrWhiteSpace(msg.Content))
        {
            await Clipboard.Default.SetTextAsync(msg.Content);
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlertAsync("Copied", "Message text copied to clipboard.", "OK");
            }
        }
        SelectedMenuMessage = null;
    }

    [RelayCommand]
    private async Task TriggerForwardFromMenuAsync()
    {
        var msg = SelectedMenuMessage;
        IsMessageMenuVisible = false;
        SelectedMenuMessage = null;
        if (msg != null)
        {
            await OpenForwardModalAsync(msg);
        }
    }

    [RelayCommand]
    private async Task TriggerDeleteFromMenuAsync()
    {
        var msg = SelectedMenuMessage;
        IsMessageMenuVisible = false;
        SelectedMenuMessage = null;
        if (msg != null)
        {
            var confirm = Application.Current?.MainPage != null
                ? await Application.Current.MainPage.DisplayAlertAsync(
                    "Delete Message",
                    "Are you sure you want to delete this message for everyone?",
                    "Delete",
                    "Cancel")
                : false;

            if (confirm && _signalRService != null)
            {
                await _signalRService.DeleteMessageAsync(msg.Id);
            }
        }
    }

    // Also alias ShowMessageOptions to ShowMessageMenu
    [RelayCommand]
    private void ShowMessageOptions(ChatMessageItem? item)
    {
        ShowMessageMenu(item);
    }

    private async Task OpenForwardModalAsync(ChatMessageItem item)
    {
        MessageToForward = item;
        ForwardStep = 0;
        SelectedForwardDepartment = null;
        ForwardDeptSearchText = string.Empty;
        ForwardDepartments.Clear();
        FilteredForwardDepartments.Clear();
        DepartmentForwardUsers.Clear();
        FilteredDepartmentForwardUsers.Clear();
        IsLoadingForwardData = true;
        IsForwardModalVisible = true;

        try
        {
            if (_apiService != null)
            {
                var depts = await _apiService.GetDepartmentsAsync();
                foreach (var d in depts)
                {
                    var fwdDept = new ForwardDepartmentItem
                    {
                        Id = d.Id,
                        Name = d.Name,
                        Description = d.Description,
                        Icon = GetDepartmentIcon(d.Name)
                    };
                    ForwardDepartments.Add(fwdDept);
                    FilteredForwardDepartments.Add(fwdDept);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoomViewModel] OpenForwardModalAsync error: {ex.Message}");
        }
        finally
        {
            IsLoadingForwardData = false;
        }
    }

    partial void OnForwardDeptSearchTextChanged(string value)
    {
        FilteredForwardDepartments.Clear();
        var q = value?.Trim().ToLowerInvariant() ?? "";
        foreach (var d in ForwardDepartments)
        {
            if (string.IsNullOrEmpty(q) || d.Name.ToLowerInvariant().Contains(q) || d.Description.ToLowerInvariant().Contains(q))
            {
                FilteredForwardDepartments.Add(d);
            }
        }
    }

    [RelayCommand]
    private async Task SelectForwardDepartmentAsync(ForwardDepartmentItem? dept)
    {
        if (dept == null || _apiService == null) return;

        SelectedForwardDepartment = dept;
        ForwardStep = 1;
        ForwardUserSearchText = string.Empty;
        DepartmentForwardUsers.Clear();
        FilteredDepartmentForwardUsers.Clear();
        IsLoadingForwardData = true;

        try
        {
            var convs = await _apiService.GetConversationsForDepartmentAsync(dept.Id);
            var seenUserIds = new HashSet<Guid>();

            foreach (var c in convs)
            {
                if (c.CreatedByUserId != Guid.Empty && seenUserIds.Add(c.CreatedByUserId))
                {
                    var userItem = new ForwardUserItem
                    {
                        UserId = c.CreatedByUserId,
                        FullName = c.CreatedByUserName,
                        DepartmentId = dept.Id,
                        DepartmentName = dept.Name,
                        Subtitle = c.Status == ConversationStatus.Active ? "Active Chat" : "Chat Closed",
                        IsOnline = c.IsOnline
                    };
                    DepartmentForwardUsers.Add(userItem);
                    FilteredDepartmentForwardUsers.Add(userItem);
                }
            }

            var allUsers = await _apiService.GetUsersAsync();
            foreach (var u in allUsers.Where(x => x.Role == UserRole.User))
            {
                if (seenUserIds.Add(u.Id))
                {
                    var userItem = new ForwardUserItem
                    {
                        UserId = u.Id,
                        FullName = u.FullName,
                        DepartmentId = dept.Id,
                        DepartmentName = dept.Name,
                        Subtitle = !string.IsNullOrWhiteSpace(u.Email) ? u.Email : "Customer",
                        IsOnline = false
                    };
                    DepartmentForwardUsers.Add(userItem);
                    FilteredDepartmentForwardUsers.Add(userItem);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatRoomViewModel] SelectForwardDepartmentAsync error: {ex.Message}");
        }
        finally
        {
            IsLoadingForwardData = false;
        }
    }

    partial void OnForwardUserSearchTextChanged(string value)
    {
        FilteredDepartmentForwardUsers.Clear();
        var q = value?.Trim().ToLowerInvariant() ?? "";
        foreach (var u in DepartmentForwardUsers)
        {
            if (string.IsNullOrEmpty(q) || u.FullName.ToLowerInvariant().Contains(q) || u.Subtitle.ToLowerInvariant().Contains(q))
            {
                FilteredDepartmentForwardUsers.Add(u);
            }
        }
    }

    [RelayCommand]
    private void StepBackToDepartments()
    {
        ForwardStep = 0;
        SelectedForwardDepartment = null;
        ForwardUserSearchText = string.Empty;
        DepartmentForwardUsers.Clear();
        FilteredDepartmentForwardUsers.Clear();
    }

    [RelayCommand]
    private void CloseForwardModal()
    {
        IsForwardModalVisible = false;
        MessageToForward = null;
        SelectedForwardDepartment = null;
        ForwardStep = 0;
    }

    [RelayCommand]
    private async Task ExecuteForwardAsync(ForwardUserItem? target)
    {
        if (target == null || MessageToForward == null || _signalRService == null) return;

        IsForwardModalVisible = false;
        var msgId = MessageToForward.Id;
        var targetDeptId = target.DepartmentId;
        var targetUserId = target.UserId;
        var targetName = target.FullName;
        var targetDeptName = target.DepartmentName;

        MessageToForward = null;
        SelectedForwardDepartment = null;
        ForwardStep = 0;

        await _signalRService.ForwardMessageAsync(msgId, targetDeptId, targetUserId);

        if (Application.Current?.MainPage != null)
        {
            await Application.Current.MainPage.DisplayAlertAsync(
                "Forwarded",
                $"Message forwarded to {targetName} in {targetDeptName}.",
                "OK"
            );
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
    private async Task ShowChatMenuOptionsAsync()
    {
        var myRole = _authService?.Role ?? _authService?.CurrentUser?.Role ?? UserRole.User;
        var isStaff = myRole == UserRole.Admin || myRole == UserRole.Manager;

        if (!isStaff) return;

        var blockAction = IsTargetUserBlocked ? "✅ Unblock User" : "🚫 Block User";
        var choice = await Application.Current?.MainPage?.DisplayActionSheetAsync(
            $"Chat Options ({CustomerName})",
            "Cancel",
            null,
            blockAction
        )!;

        if (choice == "🚫 Block User")
        {
            var confirm = await Application.Current?.MainPage?.DisplayAlertAsync(
                "Block User",
                $"Are you sure you want to block {CustomerName} from sending messages?",
                "Block",
                "Cancel"
            )!;

            if (confirm && _apiService != null && OtherUserId != Guid.Empty)
            {
                var (success, msg) = await _apiService.BlockUserAsync(OtherUserId);
                if (success)
                {
                    IsTargetUserBlocked = true;
                    await Application.Current?.MainPage?.DisplayAlertAsync("Blocked", $"{CustomerName} has been blocked.", "OK")!;
                }
                else
                {
                    await Application.Current?.MainPage?.DisplayAlertAsync("Error", msg ?? "Failed to block user.", "OK")!;
                }
            }
        }
        else if (choice == "✅ Unblock User")
        {
            if (_apiService != null && OtherUserId != Guid.Empty)
            {
                var (success, msg) = await _apiService.UnblockUserAsync(OtherUserId);
                if (success)
                {
                    IsTargetUserBlocked = false;
                    await Application.Current?.MainPage?.DisplayAlertAsync("Unblocked", $"{CustomerName} has been unblocked.", "OK")!;
                }
                else
                {
                    await Application.Current?.MainPage?.DisplayAlertAsync("Error", msg ?? "Failed to unblock user.", "OK")!;
                }
            }
        }
    }
}