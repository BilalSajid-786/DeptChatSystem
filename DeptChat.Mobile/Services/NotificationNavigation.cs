using DeptChat.Shared.Enums;

namespace DeptChat.Mobile.Services;

/// <summary>
/// Opens the right chat when a message notification is tapped (WhatsApp style).
/// The notification carries the conversation id (sent by the server since earlier releases).
/// The tap is remembered here and opened once the app is ready: after auto-login / login, when the
/// departments screen appears, or straight away if the app is already open and logged in.
/// The server checks access (same company, own chat, manager's departments); if the chat is not
/// allowed, deleted or from a previous account, the app simply stays on the departments screen.
/// </summary>
public static class NotificationNavigation
{
    private static readonly object Gate = new();
    private static Guid? _pendingConversationId;
    private static bool _opening;

    /// <summary>Remembers a tapped notification's conversation.</summary>
    public static void SetPending(Guid conversationId)
    {
        lock (Gate)
        {
            _pendingConversationId = conversationId;
        }
    }

    private static Guid? TakePending()
    {
        lock (Gate)
        {
            var id = _pendingConversationId;
            _pendingConversationId = null;
            return id;
        }
    }

    /// <summary>
    /// Opens the remembered chat if there is one and the user is logged in. Safe to call often:
    /// each tap is used once, and only one opening runs at a time.
    /// </summary>
    public static async Task OpenPendingAsync()
    {
        var services = IPlatformApplication.Current?.Services;
        var auth = services?.GetService<AuthService>();
        var api = services?.GetService<ChatApiService>();
        if (auth == null || api == null || Shell.Current == null) return;

        // Not logged in yet: keep it for after login (the departments screen calls this again).
        if (!auth.IsAuthenticated) return;

        Guid conversationId;
        lock (Gate)
        {
            if (_opening || _pendingConversationId == null) return;
            _opening = true;
        }

        try
        {
            var pending = TakePending();
            if (pending == null) return;
            conversationId = pending.Value;

            // Already looking at this chat: nothing to do.
            if (ViewModels.ChatRoomViewModel.ActiveConversationId == conversationId) return;

            var conv = await api.GetConversationByIdAsync(conversationId);
            if (conv == null) return; // not allowed, deleted, other account or offline

            var role = auth.Role ?? UserRole.User;
            var isStaff = role == UserRole.Admin || role == UserRole.Manager;

            // Staff: use the department list entry (it also says whether the customer is blocked).
            var item = conv;
            if (isStaff)
            {
                try
                {
                    var list = await api.GetConversationsForDepartmentAsync(conv.DepartmentId);
                    item = list.FirstOrDefault(c => c.Id == conversationId) ?? conv;
                }
                catch { }
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current == null) return;
                if (ViewModels.ChatRoomViewModel.ActiveConversationId == conversationId) return;

                var deptName = Uri.EscapeDataString(item.DepartmentName ?? string.Empty);

                // Same screens as opening it by hand, so Back works as usual:
                // staff: departments > customer list > chat; user: departments > chat.
                await Shell.Current.GoToAsync("//DepartmentsPage", false);
                if (isStaff)
                {
                    await Shell.Current.GoToAsync($"DepartmentConversationsPage?deptId={item.DepartmentId}&deptName={deptName}", false);
                    await Shell.Current.GoToAsync(
                        $"ChatRoomPage?deptId={item.DepartmentId}&deptName={deptName}&convId={item.Id}" +
                        $"&userName={Uri.EscapeDataString(item.CreatedByUserName ?? string.Empty)}&userId={item.CreatedByUserId}" +
                        $"&isOnline={item.IsOnline}&isBlocked={item.IsUserBlocked}");
                }
                else
                {
                    await Shell.Current.GoToAsync($"ChatRoomPage?deptId={item.DepartmentId}&deptName={deptName}&convId={item.Id}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationNavigation] {ex.Message}");
        }
        finally
        {
            lock (Gate)
            {
                _opening = false;
            }
        }
    }
}
