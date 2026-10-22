using System.Net.Http.Headers;
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
}
