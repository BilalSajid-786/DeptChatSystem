using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DeptChat.Mobile.Services;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;

namespace DeptChat.Mobile.ViewModels;

public class ChatMessageItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClientGeneratedId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string TimeFormatted { get; set; } = DateTime.Now.ToString("h:mm tt");
    public bool IsOutgoing { get; set; }
    public long SequenceNumber { get; set; }

    public bool IsVoiceMessage { get; set; }
    public int VoiceDurationSeconds { get; set; } = 5;
    public string VoiceDurationText { get; set; } = "0:05";
    public string? LocalAudioPath { get; set; }

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
    public Color PlayButtonColor => IsOutgoing ? Color.FromArgb("#059669") : Color.FromArgb("#0284C7");

    public MessageDeliveryStatus Status { get; set; } = MessageDeliveryStatus.Pending;

    private string _statusIcon = "🕒";
    public string StatusIcon
    {
        get => _statusIcon;
        set => SetProperty(ref _statusIcon, value);
    }

    private Color _statusColor = Color.FromArgb("#94A3B8");
    public Color StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    public LayoutOptions Alignment => IsOutgoing ? LayoutOptions.End : LayoutOptions.Start;
    public Color BubbleColor => IsOutgoing ? Color.FromArgb("#DCF8C6") : Colors.White;
}

[QueryProperty(nameof(DepartmentId), "deptId")]
[QueryProperty(nameof(DepartmentName), "deptName")]
[QueryProperty(nameof(ConversationIdString), "convId")]
[QueryProperty(nameof(CustomerName), "userName")]
public partial class ChatRoomViewModel : ObservableObject
{
    private readonly SignalRService? _signalRService;
    private readonly ChatApiService? _apiService;
    private readonly AuthService? _authService;
    private readonly LocalOutboxService? _outboxService;
    private readonly IAudioService? _audioService;

    public static Guid ActiveConversationId { get; set; } = Guid.Empty;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _outgoingSendLock = new(1, 1);
    private bool _isSubscribed;
    private System.Timers.Timer? _playbackTimer;
    private double _playbackElapsedSeconds;

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
        if (isSorted) return;

        var sorted = Messages.OrderBy(m => m.SequenceNumber > 0 ? m.SequenceNumber : long.MaxValue).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var currentIndex = Messages.IndexOf(sorted[i]);
            if (currentIndex != i && currentIndex >= 0)
            {
                Messages.Move(currentIndex, i);
            }
        }
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
        try
        {
            long syncFromSeq;
            lock (Messages)
            {
                syncFromSeq = GetHighestContiguousSequence(Messages.Select(m => m.SequenceNumber));
            }

            var sync = await _apiService.SyncDeltaAsync(convId, syncFromSeq);
            if (sync != null && sync.Messages.Count > 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var m in sync.Messages)
                    {
                        AddOrUpdateServerMessage(m);
                        if (_outboxService != null)
                        {
                            _ = _outboxService.SaveServerMessageAsync(
                                convId,
                                m.Id,
                                m.ClientGeneratedId,
                                m.Content,
                                m.SenderName,
                                m.SenderId == (_authService?.UserId ?? Guid.Empty),
                                m.SequenceNumber,
                                m.Status,
                                m.SentAt
                            );
                        }
                    }
                    RequestScrollToBottom?.Invoke();
                });

                if (sync.LastSequenceNumber > _lastSequenceNumber)
                {
                    _lastSequenceNumber = sync.LastSequenceNumber;
                }

                var myUserId = _authService?.UserId ?? Guid.Empty;
                var unreadIncoming = sync.Messages.Where(m => m.SenderId != myUserId).ToList();
                if (unreadIncoming.Count > 0 && _signalRService != null && _signalRService.IsConnected)
                {
                    var maxIncomingSeq = unreadIncoming.Max(m => m.SequenceNumber);
                    _ = _signalRService.MarkAsReadAsync(convId, maxIncomingSeq);
                }
            }
        }
        catch
        {
            // Network or transient sync error
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<Guid> EnsureConversationIdAsync()
    {
        if (_conversationId != Guid.Empty)
            return _conversationId;

        var completed = await Task.WhenAny(_convIdTcs.Task, Task.Delay(5000));
        if (completed == _convIdTcs.Task)
        {
            return await _convIdTcs.Task;
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

    public string DisplayTitle => !string.IsNullOrWhiteSpace(CustomerName)
        ? $"{CustomerName} • {DepartmentName}"
        : DepartmentName;

    partial void OnDepartmentNameChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));
    partial void OnCustomerNameChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    [ObservableProperty]
    private string _inputMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ChatMessageItem> _messages = new();

    public event Action? RequestScrollToBottom;

    public void TriggerScrollToBottom() => RequestScrollToBottom?.Invoke();

    // Voice message recording states
    [ObservableProperty]
    private bool _isRecordingVoice;

    [ObservableProperty]
    private string _recordingDurationText = "0:00";

    private System.Timers.Timer? _recordingTimer;
    private int _recordingSeconds;
    private string? _activeRecordingPath;
    private ChatMessageItem? _currentlyPlayingItem;

    // In-App Toast Banner properties (For cross-department alerts)
    [ObservableProperty]
    private bool _showInAppToast;

    [ObservableProperty]
    private string _inAppToastMessage = string.Empty;

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

        SubscribeToEvents();
    }

    public ChatRoomViewModel()
    {
        LoadSampleMessages();
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
            _signalRService.OnInAppToast += HandleInAppToast;
            _signalRService.OnReconnected += HandleSignalRReconnected;
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
            _signalRService.OnInAppToast -= HandleInAppToast;
            _signalRService.OnReconnected -= HandleSignalRReconnected;
        }

        Connectivity.Current.ConnectivityChanged -= HandleConnectivityChanged;
        _isSubscribed = false;
    }

    private async Task LoadLocalMessagesAsync(Guid convId)
    {
        if (_outboxService == null) return;
        var localMessages = await _outboxService.GetConversationMessagesAsync(convId);
        if (localMessages.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Messages.Clear();
                foreach (var lm in localMessages.OrderBy(m => m.SequenceNumber > 0 ? m.SequenceNumber : long.MaxValue).ThenBy(m => m.Timestamp))
                {
                    var item = MapLocalToChatItem(lm);
                    Messages.Add(item);
                    if (lm.SequenceNumber > _lastSequenceNumber)
                    {
                        _lastSequenceNumber = lm.SequenceNumber;
                    }
                }
                RequestScrollToBottom?.Invoke();
            });
        }
    }

    public async Task InitializeAsync()
    {
        SubscribeToEvents();

        var myUserId = _authService?.UserId ?? Guid.Empty;

        // 1. Check if conversation ID is already available or cached
        if (!string.IsNullOrEmpty(ConversationIdString) && Guid.TryParse(ConversationIdString, out var explicitConvId))
        {
            _conversationId = explicitConvId;
            _convIdTcs.TrySetResult(_conversationId);
        }
        else if (Guid.TryParse(DepartmentId, out var deptGuid))
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

        // 3. Perform network synchronization in background without blocking the UI
        _ = Task.Run(async () =>
        {
            try
            {
                if (_conversationId == Guid.Empty && Guid.TryParse(DepartmentId, out var deptGuid) && _apiService != null)
                {
                    var convs = await _apiService.GetConversationsForDepartmentAsync(deptGuid);
                    var conv = convs.FirstOrDefault();
                    if (conv != null)
                    {
                        _conversationId = conv.Id;
                        _convIdTcs.TrySetResult(_conversationId);
                        ActiveConversationId = _conversationId;
                        Preferences.Default.Set($"dept_conv_{deptGuid}_{myUserId}", _conversationId.ToString());
                        if (string.IsNullOrEmpty(CustomerName))
                        {
                            CustomerName = conv.CreatedByUserName;
                        }
                        await LoadLocalMessagesAsync(_conversationId);
                    }
                }
                else if (_conversationId != Guid.Empty && Guid.TryParse(DepartmentId, out var deptGuid2))
                {
                    Preferences.Default.Set($"dept_conv_{deptGuid2}_{myUserId}", _conversationId.ToString());
                }

                if (_conversationId == Guid.Empty) return;

                // Connect to SignalR and enter room
                if (_signalRService != null)
                {
                    if (!_signalRService.IsConnected && !string.IsNullOrEmpty(_authService?.Token))
                    {
                        try { await _signalRService.ConnectAsync(_authService.Token); } catch { }
                    }

                    if (_signalRService.IsConnected)
                    {
                        await _signalRService.EnterRoomAsync(_conversationId);
                    }
                }

                // Sync delta and flush outbox
                await SyncMissedMessagesAsync();
                await FlushOutboxAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatRoom] Background sync error: {ex.Message}");
            }
        });
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
            _recordingSeconds = 0;
            RecordingDurationText = "0:00";

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
    private async Task CancelVoiceRecordingAsync()
    {
        _recordingTimer?.Stop();
        IsRecordingVoice = false;
        if (_audioService != null)
        {
            await _audioService.StopRecordingAsync();
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
        IsRecordingVoice = false;

        if (_audioService == null) return;

        var path = await _audioService.StopRecordingAsync();
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
            item.PlaybackProgress = 0;
            item.CurrentPlaybackTime = item.VoiceDurationText;
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
            item.PlaybackProgress = 0;
            _playbackElapsedSeconds = 0;
            var totalDuration = Math.Max(1, item.VoiceDurationSeconds);

            _playbackTimer?.Stop();
            _playbackTimer?.Dispose();
            _playbackTimer = new System.Timers.Timer(100);
            _playbackTimer.Elapsed += (s, e) =>
            {
                _playbackElapsedSeconds += 0.1;
                if (_playbackElapsedSeconds > totalDuration)
                {
                    _playbackElapsedSeconds = totalDuration;
                }
                var progress = Math.Clamp(_playbackElapsedSeconds / totalDuration, 0.0, 1.0);
                var curSec = (int)_playbackElapsedSeconds;
                var m = curSec / 60;
                var sec = curSec % 60;
                var timeText = $"{m}:{sec:D2}";

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (item.IsPlaying)
                    {
                        item.PlaybackProgress = progress;
                        item.CurrentPlaybackTime = timeText;
                    }
                });
            };
            _playbackTimer.Start();

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
            });
        }
    }

    private Task SendMessageInternalAsync(string content, bool isVoice = false, int duration = 0, string? localPath = null)
    {
        var clientMsgId = Guid.NewGuid().ToString();

        // Optimistic UI update: instant speech bubble
        var pendingItem = new ChatMessageItem
        {
            ClientGeneratedId = clientMsgId,
            SenderName = _authService?.CurrentUser?.FullName ?? "You",
            Content = content,
            TimeFormatted = DateTime.Now.ToString("h:mm tt"),
            IsOutgoing = true,
            Status = MessageDeliveryStatus.Pending,
            StatusIcon = "🕒",
            StatusColor = Color.FromArgb("#94A3B8"),
            IsVoiceMessage = isVoice,
            VoiceDurationSeconds = duration > 0 ? duration : 5,
            VoiceDurationText = isVoice ? $"0:{duration:D2}" : string.Empty,
            CurrentPlaybackTime = isVoice ? $"0:{duration:D2}" : string.Empty,
            LocalAudioPath = localPath
        };

        Messages.Add(pendingItem);
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
                    await _outboxService.EnqueueAsync(convId, clientMsgId, content, pendingItem.SenderName);
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

    private void HandleMessageReceived(ChatMessageDto msg)
    {
        if (msg.ConversationId != _conversationId) return;

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
            // 1. Acknowledge delivery
            _ = _signalRService.AcknowledgeDeliveryAsync(msg.Id);

            // 2. Since the recipient is currently looking at this open chat screen, mark as Read in real-time!
            // This immediately turns the sender's grey ticks to BLUE ✓✓!
            if (ActiveConversationId == _conversationId)
            {
                _ = _signalRService.MarkAsReadAsync(_conversationId, msg.SequenceNumber);
            }
        }

        if (_outboxService != null)
        {
            _ = _outboxService.SaveServerMessageAsync(
                _conversationId,
                msg.Id,
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

    private void AddOrUpdateServerMessage(ChatMessageDto msg)
    {
        var isOutgoing = msg.SenderId == (_authService?.UserId ?? Guid.Empty);

        var existing = Messages.FirstOrDefault(m => m.ClientGeneratedId == msg.ClientGeneratedId || m.Id == msg.Id);
        if (existing != null)
        {
            existing.Id = msg.Id;
            existing.SequenceNumber = msg.SequenceNumber;
            UpdateStatusVisuals(existing, msg.Status);
            ReorderMessageIfNeeded(existing);
        }
        else
        {
            var newItem = new ChatMessageItem
            {
                Id = msg.Id,
                ClientGeneratedId = msg.ClientGeneratedId,
                SenderName = msg.SenderName,
                Content = msg.Content,
                TimeFormatted = msg.SentAt.ToLocalTime().ToString("h:mm tt"),
                IsOutgoing = isOutgoing,
                SequenceNumber = msg.SequenceNumber
            };

            ParseVoiceMetadata(newItem, msg.Content);
            UpdateStatusVisuals(newItem, msg.Status);

            Messages.Add(newItem);
            SortMessages();
            RequestScrollToBottom?.Invoke();
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
            SenderName = lm.SenderName,
            Content = lm.Content,
            TimeFormatted = lm.Timestamp.ToLocalTime().ToString("h:mm tt"),
            IsOutgoing = lm.IsOutgoing,
            SequenceNumber = lm.SequenceNumber
        };

        ParseVoiceMetadata(item, lm.Content);
        UpdateStatusVisuals(item, lm.Status);
        return item;
    }

    private static void ParseVoiceMetadata(ChatMessageItem item, string content)
    {
        if (content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase))
        {
            item.IsVoiceMessage = true;
            try
            {
                var inner = content.Substring(7, content.Length - 8);
                var lastColon = inner.LastIndexOf(':');
                if (lastColon > 0)
                {
                    var dur = inner.Substring(lastColon + 1);
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
        }
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
                item.StatusColor = Color.FromArgb("#94A3B8");
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
                item.StatusColor = Color.FromArgb("#34B7F1"); // WhatsApp read blue tick
                break;
        }
    }

    public async Task OnDisappearingAsync()
    {
        if (ActiveConversationId == _conversationId)
        {
            ActiveConversationId = Guid.Empty;
        }

        UnsubscribeFromEvents();

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

    private void LoadSampleMessages()
    {
        Messages = new ObservableCollection<ChatMessageItem>
        {
            new ChatMessageItem
            {
                SenderName = "Support Staff",
                Content = "Hello! How can we assist you with this department today?",
                TimeFormatted = "10:15 AM",
                IsOutgoing = false
            },
            new ChatMessageItem
            {
                SenderName = "You",
                Content = "My order #8849 arrived with broken packaging. I would like to request an exchange or replacement.",
                TimeFormatted = "10:17 AM",
                IsOutgoing = true,
                StatusIcon = "✓✓",
                StatusColor = Color.FromArgb("#34B7F1")
            }
        };
    }

    [RelayCommand]
    public async Task BackAsync()
    {
        await OnDisappearingAsync();
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
}
