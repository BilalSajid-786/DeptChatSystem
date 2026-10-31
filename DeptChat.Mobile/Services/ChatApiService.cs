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
}
