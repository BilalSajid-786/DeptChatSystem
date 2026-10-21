using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;

namespace DeptChat.Mobile.Services;

public class AuthService
{
    private const string AuthStorageKey = "auth_current_user";
    private readonly HttpClient _httpClient;
    private readonly ServerConfigService _serverConfig;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthResponseDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser != null && !string.IsNullOrEmpty(CurrentUser.Token);
    public string? Token => CurrentUser?.Token;
    public Guid? UserId => CurrentUser?.UserId;
    public UserRole? Role => CurrentUser?.Role;
    public List<Guid> DepartmentIds => CurrentUser?.DepartmentIds ?? new();

    public AuthService(HttpClient httpClient, ServerConfigService serverConfig)
    {
        _httpClient = httpClient;
        _serverConfig = serverConfig;
        TryRestoreUser();
    }

    private string BuildUrl(string path) => $"{_serverConfig.CurrentUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    public bool TryRestoreUser()
    {
        try
        {
            var json = Preferences.Default.Get(AuthStorageKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                CurrentUser = JsonSerializer.Deserialize<AuthResponseDto>(json, JsonOptions);
                return IsAuthenticated;
            }
        }
        catch { }
        return false;
    }

    public string? LastError { get; private set; }

    public async Task<bool> LoginAsync(string email, string password)
    {
        LastError = null;
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BuildUrl("api/auth/login"), new LoginRequestDto(email, password));
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    LastError = "Invalid email or password.";
                }
                else
                {
                    LastError = $"Server returned {(int)response.StatusCode}: {response.ReasonPhrase}";
                }
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            CurrentUser = JsonSerializer.Deserialize<AuthResponseDto>(content, JsonOptions);
            if (CurrentUser != null)
            {
                Preferences.Default.Set(AuthStorageKey, content);
                return true;
            }
            LastError = "Invalid server response.";
            return false;
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            LastError = $"Connection failed ({_serverConfig.CurrentUrl}): {detail}";
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string fullName, string email, string password)
    {
        LastError = null;
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BuildUrl("api/auth/register"), new RegisterRequestDto(fullName, email, password));
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                LastError = !string.IsNullOrWhiteSpace(errContent) ? errContent : $"Registration failed ({(int)response.StatusCode})";
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            CurrentUser = JsonSerializer.Deserialize<AuthResponseDto>(content, JsonOptions);
            if (CurrentUser != null)
            {
                Preferences.Default.Set(AuthStorageKey, content);
                return true;
            }
            LastError = "Invalid server response.";
            return false;
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            LastError = $"Connection failed ({_serverConfig.CurrentUrl}): {detail}";
            return false;
        }
    }

    public void Logout()
    {
        CurrentUser = null;
        try
        {
            Preferences.Default.Remove(AuthStorageKey);
        }
        catch { }
    }
}
