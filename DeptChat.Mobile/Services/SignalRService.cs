using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace DeptChat.Mobile.Services;

public class EndlessRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.PreviousRetryCount == 0) return TimeSpan.Zero;
        if (retryContext.PreviousRetryCount == 1) return TimeSpan.FromSeconds(1);
        if (retryContext.PreviousRetryCount == 2) return TimeSpan.FromSeconds(2);
        return TimeSpan.FromSeconds(3); // Retry every 3s forever
    }
}

public class SignalRService
{
    private HubConnection? _hubConnection;
    private string _baseUrl;
    private string? _authToken;

    public void UpdateBaseUrl(string newBaseUrl) => _baseUrl = newBaseUrl.TrimEnd('/');

    public event Action<ChatMessageDto>? OnMessageReceived;
    public event Action<string, Guid, long, MessageDeliveryStatus>? OnMessageAck;
    public event Action<Guid>? OnMessageDelivered;
    public event Action<Guid, long>? OnMessagesRead;
    public event Action<InAppNotificationToastDto>? OnInAppToast;
    public event Action<DepartmentDto>? OnDepartmentAdded;
    public event Action<Guid>? OnDepartmentDeleted;
    public event Action? OnReconnected;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public SignalRService(string baseUrl = "http://localhost:5051")
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task ConnectAsync(string token)
    {
        _authToken = token;

        if (_hubConnection != null)
        {
            try { await _hubConnection.DisposeAsync(); } catch { }
            _hubConnection = null;
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hubs/chat", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_authToken);
                options.HttpMessageHandlerFactory = _ => FastHttpHandler.CreateHandler();
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect(new EndlessRetryPolicy())
            .Build();

        _hubConnection.Reconnected += (connectionId) =>
        {
            OnReconnected?.Invoke();
            return Task.CompletedTask;
        };

        _hubConnection.Closed += async (error) =>
        {
            await Task.Delay(2000);
            if (!string.IsNullOrEmpty(_authToken))
            {
                try { await EnsureConnectedAsync(); } catch { }
            }
        };

        _hubConnection.On<ChatMessageDto>("ReceiveMessage", (msg) =>
        {
            OnMessageReceived?.Invoke(msg);
        });

        _hubConnection.On<string, Guid, long, MessageDeliveryStatus>("MessageAck", (clientGenId, msgId, seq, status) =>
        {
            OnMessageAck?.Invoke(clientGenId, msgId, seq, status);
        });

        _hubConnection.On<Guid>("MessageDelivered", (msgId) =>
        {
            OnMessageDelivered?.Invoke(msgId);
        });

        _hubConnection.On<Guid, long>("MessagesRead", (convId, maxSeq) =>
        {
            OnMessagesRead?.Invoke(convId, maxSeq);
        });

        _hubConnection.On<InAppNotificationToastDto>("InAppToast", (toast) =>
        {
            OnInAppToast?.Invoke(toast);
        });

        _hubConnection.On<DepartmentDto>("DepartmentAdded", (dept) =>
        {
            OnDepartmentAdded?.Invoke(dept);
        });

        _hubConnection.On<Guid>("DepartmentDeleted", (deptId) =>
        {
            OnDepartmentDeleted?.Invoke(deptId);
        });

        try
        {
            await _hubConnection.StartAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SignalR] Initial connection error: {ex.Message}");
        }

        var fcmToken = Preferences.Default.Get("fcm_device_token", string.Empty);
        if (!string.IsNullOrWhiteSpace(fcmToken) && IsConnected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _hubConnection.InvokeAsync("RegisterDeviceToken", fcmToken, "Android");
                }
                catch { }
            });
        }
    }

    public async Task<bool> EnsureConnectedAsync(string? token = null)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _authToken = token;
        }

        if (string.IsNullOrEmpty(_authToken))
            return false;

        if (IsConnected)
            return true;

        if (_hubConnection == null)
        {
            await ConnectAsync(_authToken);
            return IsConnected;
        }

        if (_hubConnection.State == HubConnectionState.Connecting || _hubConnection.State == HubConnectionState.Reconnecting)
        {
            for (int i = 0; i < 30; i++)
            {
                if (_hubConnection.State == HubConnectionState.Connected)
                    return true;
                if (_hubConnection.State == HubConnectionState.Disconnected)
                    break;
                await Task.Delay(100);
            }
        }

        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            try
            {
                await _hubConnection.StartAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        return IsConnected;
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            try
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
            }
            catch { }
            _hubConnection = null;
        }
    }

    public async Task EnterRoomAsync(Guid conversationId)
    {
        try
        {
            await EnsureConnectedAsync();
            if (IsConnected)
            {
                await _hubConnection!.InvokeAsync("EnterRoom", conversationId);
            }
        }
        catch { }
    }

    public async Task LeaveRoomAsync(Guid conversationId)
    {
        try
        {
            if (IsConnected)
            {
                await _hubConnection!.InvokeAsync("LeaveRoom", conversationId);
            }
        }
        catch { }
    }

    public async Task<bool> SendMessageAsync(Guid conversationId, string clientGeneratedId, string content)
    {
        try
        {
            var connected = await EnsureConnectedAsync();
            if (connected && _hubConnection != null)
            {
                var dto = new SendMessageDto(conversationId, clientGeneratedId, content);
                await _hubConnection.SendAsync("SendMessage", dto);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SignalR] SendMessageAsync error: {ex.Message}");
        }
        return false;
    }

    public async Task AcknowledgeDeliveryAsync(Guid messageId)
    {
        try
        {
            if (IsConnected)
            {
                await _hubConnection!.SendAsync("AcknowledgeDelivery", messageId);
            }
        }
        catch { }
    }

    public async Task MarkAsReadAsync(Guid conversationId, long maxSequenceNumber)
    {
        try
        {
            if (IsConnected)
            {
                await _hubConnection!.SendAsync("MarkAsRead", conversationId, maxSequenceNumber);
            }
        }
        catch { }
    }

    public async Task RegisterDeviceTokenAsync(string token, string platform = "Android")
    {
        try
        {
            await EnsureConnectedAsync();
            if (IsConnected && !string.IsNullOrWhiteSpace(token))
            {
                await _hubConnection!.SendAsync("RegisterDeviceToken", token, platform);
            }
        }
        catch { }
    }
}
