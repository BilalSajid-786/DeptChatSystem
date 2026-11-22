using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeptChat.Shared.DTOs;
using Microsoft.AspNetCore.Components.Forms;

namespace DeptChat.Web.Client.Services;

public sealed class ApiException : Exception
{
    public ApiException(string message, int status) : base(message) => Status = status;
    public int Status { get; }
}

/// <summary>
/// REST client for the existing DeptChat API (same endpoints the Android app uses).
/// Errors from the API come back as { "message": "..." } and are surfaced as ApiException.
/// A 401 on an authenticated call means the session was revoked or expired: sign out locally.
/// </summary>
public sealed class ApiClient
{
    public const long MaxUploadBytes = 25L * 1024 * 1024;

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    public static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".txt", ".xlsx", ".xls", ".csv", ".zip", ".rar", ".7z", ".mp3", ".m4a", ".wav", ".aac" };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AuthSession _auth;

    public ApiClient(HttpClient http, AuthSession auth)
    {
        _http = http;
        _auth = auth;
    }

    // ---------- auth & profile ----------
    public Task<AuthResponseDto> LoginAsync(LoginRequestDto dto) => SendAsync<AuthResponseDto>(HttpMethod.Post, "api/auth/login", dto, signOutOn401: false);
    public Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto) => SendAsync<AuthResponseDto>(HttpMethod.Post, "api/auth/register", dto, signOutOn401: false);
    public Task<UserProfileDto> GetProfileAsync() => SendAsync<UserProfileDto>(HttpMethod.Get, "api/auth/profile");
    public Task<UserProfileDto> UpdateProfileAsync(UpdateProfileRequestDto dto) => SendAsync<UserProfileDto>(HttpMethod.Put, "api/auth/profile", dto);
    public Task<string> ChangePasswordAsync(ChangePasswordRequestDto dto) => SendForMessageAsync(HttpMethod.Post, "api/auth/change-password", dto);
    public Task<List<UserSessionDto>> GetSessionsAsync() => SendAsync<List<UserSessionDto>>(HttpMethod.Get, "api/auth/sessions");
    public Task<string> RevokeSessionAsync(Guid id) => SendForMessageAsync(HttpMethod.Post, $"api/auth/sessions/{id}/revoke");
    public Task<string> RevokeOtherSessionsAsync() => SendForMessageAsync(HttpMethod.Post, "api/auth/sessions/revoke-others");

    // ---------- departments ----------
    public Task<List<DepartmentDto>> GetDepartmentsAsync() => SendAsync<List<DepartmentDto>>(HttpMethod.Get, "api/departments");
    public Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentDto dto) => SendAsync<DepartmentDto>(HttpMethod.Post, "api/departments", dto);
    public Task<string> DeleteDepartmentAsync(Guid id) => SendForMessageAsync(HttpMethod.Delete, $"api/departments/{id}");
    public Task<string> AssignManagerAsync(Guid departmentId, Guid userId)
        => SendForMessageAsync(HttpMethod.Post, $"api/departments/{departmentId}/managers", new AssignManagerDto(departmentId, userId));

    // ---------- conversations & messages ----------
    public Task<List<ConversationSummaryDto>> GetDepartmentConversationsAsync(Guid departmentId)
        => SendAsync<List<ConversationSummaryDto>>(HttpMethod.Get, $"api/conversations/department/{departmentId}");

    public Task<ConversationSummaryDto> GetConversationAsync(Guid conversationId)
        => SendAsync<ConversationSummaryDto>(HttpMethod.Get, $"api/conversations/{conversationId}");

    public Task<PaginatedMessagesDto> GetMessagesAsync(Guid conversationId, long? beforeSequenceNumber, int pageSize = 30)
        => SendAsync<PaginatedMessagesDto>(HttpMethod.Get, beforeSequenceNumber.HasValue
            ? $"api/conversations/{conversationId}/messages?beforeSequenceNumber={beforeSequenceNumber.Value}&pageSize={pageSize}"
            : $"api/conversations/{conversationId}/messages?pageSize={pageSize}");

    public Task<ConversationSyncResponseDto> SyncAsync(Guid conversationId, long sinceSequenceNumber)
        => SendAsync<ConversationSyncResponseDto>(HttpMethod.Get, $"api/conversations/{conversationId}/sync?sinceSequenceNumber={sinceSequenceNumber}");

    // ---------- personal pins & clear chat ----------
    /// <summary>My own pinned messages in this chat (nobody else sees them), newest pin first.</summary>
    public Task<List<ChatMessageDto>> GetPinsAsync(Guid conversationId)
        => SendAsync<List<ChatMessageDto>>(HttpMethod.Get, $"api/conversations/{conversationId}/pins");

    public Task<string> PinMessageAsync(Guid conversationId, Guid messageId)
        => SendForMessageAsync(HttpMethod.Post, $"api/conversations/{conversationId}/messages/{messageId}/pin");

    public Task<string> UnpinMessageAsync(Guid conversationId, Guid messageId)
        => SendForMessageAsync(HttpMethod.Delete, $"api/conversations/{conversationId}/messages/{messageId}/pin");

    /// <summary>Admins/managers: pins a customer chat at the top of my own department chat list.</summary>
    public Task<string> PinConversationAsync(Guid conversationId)
        => SendForMessageAsync(HttpMethod.Post, $"api/conversations/{conversationId}/pin");

    public Task<string> UnpinConversationAsync(Guid conversationId)
        => SendForMessageAsync(HttpMethod.Delete, $"api/conversations/{conversationId}/pin");

    /// <summary>User: clears the chat for themselves. Admin / Manager: clears it for everyone.</summary>
    public Task<ClearChatResultDto> ClearChatAsync(Guid conversationId, bool forEveryone = true)
        => SendAsync<ClearChatResultDto>(HttpMethod.Post, $"api/conversations/{conversationId}/clear?forEveryone={(forEveryone ? "true" : "false")}");

    /// <summary>The company's disappearing-messages time in seconds (0 = off).</summary>
    public Task<DisappearingMessagesSettingDto> GetDisappearingMessagesAsync()
        => SendAsync<DisappearingMessagesSettingDto>(HttpMethod.Get, "api/conversations/disappearing-messages");

    /// <summary>Admins only: sets it for every chat in the company.</summary>
    public Task<DisappearingMessagesSettingDto> SetDisappearingMessagesAsync(int seconds)
        => SendAsync<DisappearingMessagesSettingDto>(HttpMethod.Put, "api/conversations/disappearing-messages", new DisappearingMessagesSettingDto(seconds));

    public Task<List<AvailableDepartmentUserDto>> GetAvailableUsersAsync(Guid departmentId)
        => SendAsync<List<AvailableDepartmentUserDto>>(HttpMethod.Get, $"api/conversations/department/{departmentId}/available-users");

    public Task<ConversationSummaryDto> StartConversationWithUserAsync(Guid departmentId, Guid userId)
        => SendAsync<ConversationSummaryDto>(HttpMethod.Post, "api/conversations/start-with-user", new StartConversationWithUserRequest(departmentId, userId));

    public async Task<AttachmentUploadResultDto> UploadAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var stream = file.OpenReadStream(MaxUploadBytes, cancellationToken);
        var part = new StreamContent(stream);
        part.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(part, "file", file.Name);
        using var response = await SendRawAsync(HttpMethod.Post, "api/attachments/upload", content, signOutOn401: true, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AttachmentUploadResultDto>(JsonOptions, cancellationToken))!;
    }

    // ---------- users (Admin / Manager) ----------
    public Task<List<UserSummaryDto>> GetUsersAsync() => SendAsync<List<UserSummaryDto>>(HttpMethod.Get, "api/users");
    public Task<List<UserSummaryDto>> GetManagersAsync() => SendAsync<List<UserSummaryDto>>(HttpMethod.Get, "api/users/managers");
    public Task<UserSummaryDto> CreateUserAsync(CreateUserRequestDto dto) => SendAsync<UserSummaryDto>(HttpMethod.Post, "api/users", dto);
    public Task<string> BlockUserAsync(Guid id) => SendForMessageAsync(HttpMethod.Post, $"api/users/{id}/block", new BlockUserRequestDto());
    public Task<string> UnblockUserAsync(Guid id) => SendForMessageAsync(HttpMethod.Post, $"api/users/{id}/unblock");
    // Companies (platform admins only; the server returns 403 to everyone else)
    public Task<List<ClientSummaryDto>> GetClientsAsync() => SendAsync<List<ClientSummaryDto>>(HttpMethod.Get, "api/clients");
    public Task<string> BlockClientAsync(Guid id) => SendForMessageAsync(HttpMethod.Post, $"api/clients/{id}/block");
    public Task<string> UnblockClientAsync(Guid id) => SendForMessageAsync(HttpMethod.Post, $"api/clients/{id}/unblock");

    public Task<string> ResetPasswordAsync(Guid id, string newPassword)
        => SendForMessageAsync(HttpMethod.Post, $"api/users/{id}/reset-password", new AdminResetPasswordRequestDto(newPassword));

    /// <summary>Uploaded files are served by the backend under /uploads (proxied by the web host).</summary>
    public static string MediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        return "/" + url.TrimStart('/');
    }

    // ---------- plumbing ----------
    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null, bool signOutOn401 = true)
    {
        using var response = await SendRawAsync(method, url, body == null ? null : JsonContent.Create(body, options: JsonOptions), signOutOn401);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return result ?? throw new ApiException("The server sent an empty response.", (int)response.StatusCode);
    }

    private async Task<string> SendForMessageAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendRawAsync(method, url, body == null ? null : JsonContent.Create(body, options: JsonOptions), signOutOn401: true);
        return await ReadMessageAsync(response) ?? "Done.";
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, HttpContent? content, bool signOutOn401, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (_auth.User is { } user)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ApiException("Can't reach the server. Check your connection and try again.", 0);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var message = await ReadMessageAsync(response);
        var status = (int)response.StatusCode;
        response.Dispose();

        if (response.StatusCode == HttpStatusCode.Unauthorized && signOutOn401 && _auth.IsSignedIn)
        {
            await _auth.SignOutAsync("Your session has ended. Please sign in again.");
            throw new ApiException("Your session has ended. Please sign in again.", status);
        }

        throw new ApiException(message ?? DefaultMessage(response.StatusCode), status);
    }

    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var m) &&
                m.ValueKind == JsonValueKind.String)
            {
                return m.GetString();
            }
        }
        catch
        {
            // Not JSON (e.g. an empty 403): fall back to a generic message.
        }
        return null;
    }

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => "You don't have access to this.",
        HttpStatusCode.NotFound => "Not found. It may have been removed.",
        HttpStatusCode.RequestEntityTooLarge => "The file is too large (25 MB maximum).",
        HttpStatusCode.Unauthorized => "Sign-in failed.",
        _ => $"Something went wrong (error {(int)status}). Please try again."
    };
}
