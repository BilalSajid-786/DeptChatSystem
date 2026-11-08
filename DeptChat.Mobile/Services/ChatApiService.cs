using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeptChat.Shared.DTOs;

namespace DeptChat.Mobile.Services;

public class ChatApiService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;
    private readonly ServerConfigService _serverConfig;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ChatApiService(HttpClient httpClient, AuthService authService, ServerConfigService serverConfig)
    {
        _httpClient = httpClient;
        _authService = authService;
        _serverConfig = serverConfig;

        try
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("X-App-Version"))
            {
                _httpClient.DefaultRequestHeaders.Add("X-App-Version", AppInfo.Current.VersionString);
                _httpClient.DefaultRequestHeaders.Add("X-App-Build", AppInfo.Current.BuildString);
            }
        }
        catch { }
    }

    private string BuildUrl(string path) => $"{_serverConfig.CurrentUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private void AttachBearerToken()
    {
        if (!string.IsNullOrEmpty(_authService.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authService.Token);
        }
    }

    public async Task<List<DepartmentDto>> GetDepartmentsAsync()
    {
        AttachBearerToken();
        var response = await _httpClient.GetAsync(BuildUrl("api/departments"));
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<DepartmentDto>>(content, JsonOptions) ?? new();
    }

    public async Task<List<ConversationSummaryDto>> GetConversationsForDepartmentAsync(Guid departmentId)
    {
        AttachBearerToken();
        var response = await _httpClient.GetAsync(BuildUrl($"api/conversations/department/{departmentId}"));
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<ConversationSummaryDto>>(content, JsonOptions) ?? new();
    }

    public async Task<ConversationSyncResponseDto?> SyncDeltaAsync(Guid conversationId, long sinceSequenceNumber)
    {
        AttachBearerToken();
        var response = await _httpClient.GetAsync(BuildUrl($"api/conversations/{conversationId}/sync?sinceSequenceNumber={sinceSequenceNumber}"));
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ConversationSyncResponseDto>(content, JsonOptions);
    }

    public async Task<PaginatedMessagesDto?> GetMessagesAsync(Guid conversationId, long? beforeSequenceNumber = null, int pageSize = 25)
    {
        AttachBearerToken();
        var url = beforeSequenceNumber.HasValue && beforeSequenceNumber.Value > 0
            ? BuildUrl($"api/conversations/{conversationId}/messages?beforeSequenceNumber={beforeSequenceNumber.Value}&pageSize={pageSize}")
            : BuildUrl($"api/conversations/{conversationId}/messages?pageSize={pageSize}");

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PaginatedMessagesDto>(content, JsonOptions);
    }

    public async Task<DepartmentDto?> CreateDepartmentAsync(string name, string description)
    {
        AttachBearerToken();
        var dto = new CreateDepartmentDto(name, description);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(BuildUrl("api/departments"), content);
        response.EnsureSuccessStatusCode();
        var responseString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<DepartmentDto>(responseString, JsonOptions);
    }

    public async Task<(bool Success, string? ErrorMessage, UserSummaryDto? User)> CreateUserAsync(CreateUserRequestDto dto)
    {
        try
        {
            AttachBearerToken();
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(BuildUrl("api/users"), content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var createdUser = JsonSerializer.Deserialize<UserSummaryDto>(responseString, JsonOptions);
                return (true, null, createdUser);
            }

            try
            {
                using var doc = JsonDocument.Parse(responseString);
                if (doc.RootElement.TryGetProperty("message", out var msgElement))
                {
                    return (false, msgElement.GetString(), null);
                }
            }
            catch { }

            return (false, $"Failed to create user ({(int)response.StatusCode})", null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}", null);
        }
    }

    public async Task<List<UserSummaryDto>> GetUsersAsync()
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.GetAsync(BuildUrl("api/users"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<UserSummaryDto>>(json, JsonOptions) ?? new();
            }
        }
        catch { }
        return new();
    }

    public async Task<bool> RegisterDeviceTokenAsync(string token, string platform = "Android")
    {
        try
        {
            AttachBearerToken();
            var dto = new RegisterDeviceTokenDto(token, platform);
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(BuildUrl("api/auth/device-token"), content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteDepartmentAsync(Guid departmentId)
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.DeleteAsync(BuildUrl($"api/departments/{departmentId}"));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AttachmentUploadResultDto?> UploadAttachmentAsync(Stream fileStream, string fileName, string contentType)
    {
        try
        {
            AttachBearerToken();
            using var formData = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            formData.Add(streamContent, "file", fileName);

            var response = await _httpClient.PostAsync(BuildUrl("api/attachments/upload"), formData);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<AttachmentUploadResultDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatApiService] UploadAttachment error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<AvailableDepartmentUserDto>> GetAvailableUsersForDepartmentAsync(Guid departmentId)
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.GetAsync(BuildUrl($"api/conversations/department/{departmentId}/available-users"));
            if (!response.IsSuccessStatusCode) return new List<AvailableDepartmentUserDto>();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<AvailableDepartmentUserDto>>(content, JsonOptions) ?? new List<AvailableDepartmentUserDto>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatApiService] GetAvailableUsers error: {ex.Message}");
            return new List<AvailableDepartmentUserDto>();
        }
    }

    public async Task<ConversationSummaryDto?> StartConversationWithUserAsync(Guid departmentId, Guid targetUserId)
    {
        try
        {
            AttachBearerToken();
            var payload = new StartConversationWithUserRequest(departmentId, targetUserId);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(BuildUrl("api/conversations/start-with-user"), stringContent);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ConversationSummaryDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatApiService] StartConversationWithUser error: {ex.Message}");
            return null;
        }
    }

    public string ResolveFullMediaUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        return $"{_serverConfig.CurrentUrl.TrimEnd('/')}/{url.TrimStart('/')}";
    }

    public async Task<UserProfileDto?> GetProfileAsync()
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.GetAsync(BuildUrl("api/auth/profile"));
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<UserProfileDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatApiService] GetProfile error: {ex.Message}");
            return null;
        }
    }

    public async Task<UserProfileDto?> UpdateProfileAsync(string fullName, string? phoneNumber, string? address)
    {
        try
        {
            AttachBearerToken();
            var payload = new UpdateProfileRequestDto(fullName, phoneNumber, address);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(BuildUrl("api/auth/profile"), stringContent);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<UserProfileDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatApiService] UpdateProfile error: {ex.Message}");
            return null;
        }
    }

    public async Task<(bool Success, string? Message)> BlockUserAsync(Guid userId, string? reason = null)
    {
        try
        {
            AttachBearerToken();
            var dto = new BlockUserRequestDto(reason);
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(BuildUrl($"api/users/{userId}/block"), content);
            var responseString = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return (true, "User blocked successfully.");
            }
            try
            {
                using var doc = JsonDocument.Parse(responseString);
                if (doc.RootElement.TryGetProperty("message", out var msgElement))
                {
                    return (false, msgElement.GetString());
                }
            }
            catch { }
            return (false, "Failed to block user.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Message)> UnblockUserAsync(Guid userId)
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.PostAsync(BuildUrl($"api/users/{userId}/unblock"), null);
            var responseString = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return (true, "User unblocked successfully.");
            }
            try
            {
                using var doc = JsonDocument.Parse(responseString);
                if (doc.RootElement.TryGetProperty("message", out var msgElement))
                {
                    return (false, msgElement.GetString());
                }
            }
            catch { }
            return (false, "Failed to unblock user.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<List<UserSummaryDto>> GetManagersAsync()
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.GetAsync(BuildUrl("api/users/managers"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<UserSummaryDto>>(json, JsonOptions) ?? new();
            }
        }
        catch { }
        return new();
    }

    public async Task<List<ClientSummaryDto>> GetClientsAsync()
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.GetAsync(BuildUrl("api/clients"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<ClientSummaryDto>>(json, JsonOptions) ?? new();
            }
        }
        catch { }
        return new();
    }

    public async Task<(bool Success, string? Message)> BlockClientAsync(Guid clientId)
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.PostAsync(BuildUrl($"api/clients/{clientId}/block"), null);
            return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "Client blocked." : "Failed to block client.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Message)> UnblockClientAsync(Guid clientId)
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.PostAsync(BuildUrl($"api/clients/{clientId}/unblock"), null);
            return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "Client unblocked." : "Failed to unblock client.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<bool> LogoutAsync()
    {
        try
        {
            AttachBearerToken();
            var response = await _httpClient.PostAsync(BuildUrl("api/auth/logout"), null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatApiService] LogoutAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Success, string? Message)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            AttachBearerToken();
            var payload = new ChangePasswordRequestDto(currentPassword, newPassword);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(BuildUrl("api/auth/change-password"), content);
            var respStr = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return (true, "Password updated successfully.");
            }
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(respStr);
                if (doc.RootElement.TryGetProperty("message", out var msgProp))
                {
                    return (false, msgProp.GetString() ?? "Failed to update password.");
                }
            }
            catch { }
            return (false, string.IsNullOrWhiteSpace(respStr) ? "Failed to update password." : respStr);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Message)> AdminResetPasswordAsync(Guid userId, string newPassword)
    {
        try
        {
            AttachBearerToken();
            var payload = new AdminResetPasswordRequestDto(newPassword);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(BuildUrl($"api/users/{userId}/reset-password"), content);
            var respStr = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return (true, "Password reset successfully.");
            }
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(respStr);
                if (doc.RootElement.TryGetProperty("message", out var msgProp))
                {
                    return (false, msgProp.GetString() ?? "Failed to reset password.");
                }
            }
            catch { }
            return (false, string.IsNullOrWhiteSpace(respStr) ? "Failed to reset password." : respStr);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
