using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DeptChat.Web.Client.Services;

/// <summary>
/// One SignalR connection per browser tab to the existing /hubs/chat hub.
/// Method names and argument shapes match DeptChat.WebApi/Hubs/ChatHub.cs and the
/// Android SignalRService. Nothing here changes the hub contract.
/// </summary>
public sealed class ChatHubClient : IAsyncDisposable
{
    private readonly NavigationManager _nav;
    private readonly AuthSession _auth;
    private HubConnection? _connection;
    private CancellationTokenSource? _startCts;

    public ChatHubClient(NavigationManager nav, AuthSession auth)
    {
        _nav = nav;
        _auth = auth;
    }

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;
    public bool IsConnected => State == HubConnectionState.Connected;

    public event Action? StateChanged;
    /// <summary>Raised after the first connect and after every reconnect: rejoin rooms, sync, flush outbox.</summary>
    public event Func<Task>? Connected;

    public event Action<ChatMessageDto>? MessageReceived;
    public event Action<string, Guid, long, MessageDeliveryStatus>? MessageAck;
    public event Action<Guid>? MessageDelivered;
    public event Action<Guid, long>? MessagesRead;
    public event Action<InAppNotificationToastDto>? InAppToast;
    public event Action<DepartmentDto>? DepartmentAdded;
    public event Action<Guid>? DepartmentDeleted;
    public event Action<Guid, Guid, bool>? UserPresenceChanged;
    public event Action<Guid, Guid, bool, bool, DateTime?>? UserPresenceState;
    public event Action<Guid, Guid, bool>? DepartmentPresenceState;
    public event Action<Guid, Guid, string, bool>? UserTyping;
    public event Action<Guid, Guid>? MessageDeleted;
    public event Action<Guid, Guid, Guid>? MessageForwarded;
    // Clear chat: conversationId, cleared up to this sequence number, for everyone (or only for me)
    public event Action<Guid, long, bool>? ConversationCleared;
    // My personal pins changed on another device: conversationId
    public event Action<Guid>? PinsChanged;
    // My pinned customer chats changed (any of my devices or tabs): departmentId
    public event Action<Guid>? ConversationPinsChanged;
    // Disappearing messages: the server emptied messages of a chat (conversationId)
    public event Action<Guid>? MessagesExpired;
    // Disappearing messages: an admin changed the company's time (seconds, 0 = off)
    public event Action<int>? DisappearingMessagesChanged;
    public event Action<string>? ErrorMessage;
    public event Action<Guid, bool>? UserOnlineStatusChanged;
    public event Action<string>? ForceLogout;

    public async Task StartAsync()
    {
        if (_connection != null || !_auth.IsSignedIn) return;

        var connection = new HubConnectionBuilder()
            .WithUrl(_nav.ToAbsoluteUri("hubs/chat"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_auth.User?.Token);
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        // Same timings as the Android app.
        connection.ServerTimeout = TimeSpan.FromSeconds(90);
        connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

        connection.On<ChatMessageDto>("ReceiveMessage", m => MessageReceived?.Invoke(m));
        connection.On<string, Guid, long, MessageDeliveryStatus>("MessageAck", (c, id, seq, st) => MessageAck?.Invoke(c, id, seq, st));
        connection.On<Guid>("MessageDelivered", id => MessageDelivered?.Invoke(id));
        connection.On<Guid, long>("MessagesRead", (c, seq) => MessagesRead?.Invoke(c, seq));
        connection.On<InAppNotificationToastDto>("InAppToast", t => InAppToast?.Invoke(t));
        connection.On<DepartmentDto>("DepartmentAdded", d => DepartmentAdded?.Invoke(d));
        connection.On<Guid>("DepartmentDeleted", id => DepartmentDeleted?.Invoke(id));
        connection.On<Guid, Guid, bool>("UserPresenceChanged", (c, u, inRoom) => UserPresenceChanged?.Invoke(c, u, inRoom));
        connection.On<Guid, Guid, bool, bool, DateTime?>("UserPresenceState", (c, u, inRoom, online, seen) => UserPresenceState?.Invoke(c, u, inRoom, online, seen));
        connection.On<Guid, Guid, bool>("DepartmentPresenceState", (c, d, online) => DepartmentPresenceState?.Invoke(c, d, online));
        connection.On<Guid, Guid, string, bool>("UserTyping", (c, u, name, typing) => UserTyping?.Invoke(c, u, name, typing));
        connection.On<Guid, Guid>("MessageDeleted", (c, m) => MessageDeleted?.Invoke(c, m));
        connection.On<Guid, Guid, Guid>("MessageForwarded", (s, c, f) => MessageForwarded?.Invoke(s, c, f));
        connection.On<Guid, long, bool>("ConversationCleared", (c, seq, all) => ConversationCleared?.Invoke(c, seq, all));
        connection.On<Guid>("PinsChanged", c => PinsChanged?.Invoke(c));
        connection.On<Guid>("ConversationPinsChanged", d => ConversationPinsChanged?.Invoke(d));
        connection.On<Guid>("MessagesExpired", c => MessagesExpired?.Invoke(c));
        connection.On<int>("DisappearingMessagesChanged", s => DisappearingMessagesChanged?.Invoke(s));
        connection.On<string>("ErrorMessage", e => ErrorMessage?.Invoke(e));
        connection.On<Guid, bool>("UserOnlineStatusChanged", (u, online) => UserOnlineStatusChanged?.Invoke(u, online));
        connection.On<string>("ForceLogout", r => ForceLogout?.Invoke(r));

        connection.Reconnecting += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
        connection.Reconnected += async _ => { StateChanged?.Invoke(); await RaiseConnectedAsync(); };
        connection.Closed += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };

        _connection = connection;
        _startCts = new CancellationTokenSource();
        _ = ConnectLoopAsync(connection, _startCts.Token);
        await Task.CompletedTask;
    }

    /// <summary>Automatic reconnect only covers drops after a successful start, so retry the first start too.</summary>
    private async Task ConnectLoopAsync(HubConnection connection, CancellationToken token)
    {
        var attempt = 0;
        while (!token.IsCancellationRequested && connection.State == HubConnectionState.Disconnected)
        {
            try
            {
                StateChanged?.Invoke();
                await connection.StartAsync(token);
                StateChanged?.Invoke();
                await RaiseConnectedAsync();
                return;
            }
            catch when (!token.IsCancellationRequested)
            {
                attempt++;
                StateChanged?.Invoke();
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5))));
                try { await Task.Delay(delay, token); } catch { return; }
            }
            catch
            {
                return;
            }
        }
    }

    private async Task RaiseConnectedAsync()
    {
        var handlers = Connected;
        if (handlers == null) return;
        foreach (Func<Task> handler in handlers.GetInvocationList())
        {
            try { await handler(); } catch { /* one listener failing must not stop the others */ }
        }
    }

    public async Task StopAsync()
    {
        _startCts?.Cancel();
        _startCts = null;
        var connection = _connection;
        _connection = null;
        if (connection != null)
        {
            try { await connection.StopAsync(); } catch { }
            await connection.DisposeAsync();
        }
        StateChanged?.Invoke();
    }

    // ---------- hub methods ----------
    public Task<bool> EnterRoomAsync(Guid conversationId) => TryInvokeAsync("EnterRoom", conversationId);
    public Task<bool> LeaveRoomAsync(Guid conversationId) => TryInvokeAsync("LeaveRoom", conversationId);
    public Task<bool> SendTypingAsync(Guid conversationId, bool isTyping) => TryInvokeAsync("SendTyping", conversationId, isTyping);
    public Task<bool> SendMessageAsync(SendMessageDto dto) => TryInvokeAsync("SendMessage", dto);
    public Task<bool> AcknowledgeDeliveryAsync(Guid messageId) => TryInvokeAsync("AcknowledgeDelivery", messageId);
    public Task<bool> MarkAsReadAsync(Guid conversationId, long maxSequenceNumber) => TryInvokeAsync("MarkAsRead", conversationId, maxSequenceNumber);
    public Task<bool> DeleteMessageAsync(Guid messageId) => TryInvokeAsync("DeleteMessage", messageId);
    public Task<bool> ForwardMessageAsync(Guid messageId, Guid targetDepartmentId, Guid targetUserId)
        => TryInvokeAsync("ForwardMessage", messageId, targetDepartmentId, targetUserId);

    public async Task<UserPresenceDto?> GetPresenceAsync(Guid targetUserId, Guid? conversationId)
    {
        var connection = _connection;
        if (connection?.State != HubConnectionState.Connected) return null;
        try { return await connection.InvokeAsync<UserPresenceDto>("GetPresence", targetUserId, conversationId); }
        catch { return null; }
    }

    private async Task<bool> TryInvokeAsync(string method, params object?[] args)
    {
        var connection = _connection;
        if (connection?.State != HubConnectionState.Connected) return false;
        try
        {
            await connection.InvokeCoreAsync(method, args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
            => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5))));
    }
}
